using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using OpenMono.Config;
using OpenMono.Llm;
using OpenMono.Session;
using OpenMono.Utils;

namespace OpenMono.Tests.Llm;

public class OpenAiCompatClientTests
{
    [Theory]
    [InlineData("http://localhost:7474", "http://localhost:7474/v1/chat/completions")]
    [InlineData("https://token-plan-sgp.xiaomimimo.com/v1", "https://token-plan-sgp.xiaomimimo.com/v1/chat/completions")]
    [InlineData("https://example.com/v1/", "https://example.com/v1/chat/completions")]
    [InlineData("https://example.com/api/v1", "https://example.com/api/v1/chat/completions")]
    public void Resource_PreservesApiBasePath(string endpoint, string expected)
    {
        OpenAiEndpoint.Resource(endpoint, "chat/completions").Should().Be(expected);
        OpenAiEndpoint.Resource(endpoint, "models").Should().Be(expected.Replace("chat/completions", "models"));
    }

    [Fact]
    public async Task StreamChatAsync_MimoHandlesNullDeltasAndFragmentedToolCalls()
    {
        const string sse = """
            data:{"choices":[{"delta":{"content":null,"reasoning_content":null,"tool_calls":null}}]}

            data: {"choices":[{"delta":{"reasoning_content":"Inspect the file first.","tool_calls":null}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","function":{"name":"FileRead","arguments":"{\"path\":"}}]}}]}

            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"README.md\"}"}}]},"finish_reason":"tool_calls"}]}

            data: {"choices":[],"usage":{"prompt_tokens":12,"completion_tokens":8}}

            data: [DONE]

            """;
        var handler = new CapturingHandler(sse);
        using var client = new OpenAiCompatClient(new LlmConfig
        {
            Provider = "openai-compatible",
            Endpoint = "https://token-plan-sgp.xiaomimimo.com/v1",
            Model = "mimo-v2.5-pro",
        }, new HttpClient(handler))
        { ApiKey = "test-key" };
        var chunks = new List<StreamChunk>();
        await foreach (var chunk in client.StreamChatAsync(
            [new Message { Role = MessageRole.User, Content = "Read the README" }], null,
            new LlmOptions { EnableThinking = true }, CancellationToken.None))
            chunks.Add(chunk);

        handler.Url.Should().Be("https://token-plan-sgp.xiaomimimo.com/v1/chat/completions");
        handler.Authorization.Should().Be("Bearer test-key");
        chunks.Should().Contain(c => c.ThinkingDelta == "Inspect the file first.");
        chunks.Single(c => c.ToolCallDelta is not null).ToolCallDelta.Should().BeEquivalentTo(
            new ToolCall { Id = "call_1", Name = "FileRead", Arguments = "{\"path\":\"README.md\"}" });
        chunks.Should().Contain(c => c.Usage != null && c.Usage.TotalTokens == 20);
        chunks.Last().IsComplete.Should().BeTrue();
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("model").GetString().Should().Be("mimo-v2.5-pro");
        body.RootElement.GetProperty("thinking").GetProperty("type").GetString().Should().Be("enabled");
        body.RootElement.TryGetProperty("temperature", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("top_p", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("max_completion_tokens", out _).Should().BeTrue();
        body.RootElement.TryGetProperty("max_tokens", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildRequestBody_LlamaParametersAreOnlySentToLocalProvider(bool local)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(OpenAiCompatClient.BuildRequestBody(
            [], null, new LlmOptions { EnableThinking = true }, "test-model", local), JsonOptions.Default));
        foreach (var key in new[] { "top_k", "min_p", "repetition_penalty", "chat_template_kwargs" })
            doc.RootElement.TryGetProperty(key, out _).Should().Be(local);
    }

    [Fact]
    public void BuildRequestBody_MimoPreservesReasoningAcrossSessionSerialization()
    {
        var history = new List<Message>
        {
            new() { Role = MessageRole.User, Content = "Inspect" },
            new()
            {
                Role = MessageRole.Assistant, ReasoningContent = "Read the file before answering.",
                ToolCalls = [new ToolCall { Id = "call_1", Name = "FileRead", Arguments = "{}" }],
            },
            new() { Role = MessageRole.Tool, ToolCallId = "call_1", Content = "file contents" },
        };
        var restored = JsonSerializer.Deserialize<List<Message>>(JsonSerializer.Serialize(history, JsonOptions.Default), JsonOptions.Default)!;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(OpenAiCompatClient.BuildRequestBody(
            restored, null, new LlmOptions { EnableThinking = false }, "mimo-v2.5-pro", false), JsonOptions.Default));
        doc.RootElement.GetProperty("messages")[1].GetProperty("reasoning_content").GetString()
            .Should().Be(history[1].ReasoningContent);
        doc.RootElement.GetProperty("thinking").GetProperty("type").GetString().Should().Be("disabled");
        TokenEstimate.SerializePayload(restored).Should().Contain("reasoning_content");
        TokenEstimator.EstimateMessage(restored[1]).Should().BeGreaterThan(
            TokenEstimator.EstimateMessage(restored[1] with { ReasoningContent = null }));
    }

    private sealed class CapturingHandler(string response) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "text/event-stream"),
            };
        }
    }
}
