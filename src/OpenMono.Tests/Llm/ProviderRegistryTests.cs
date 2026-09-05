using FluentAssertions;
using OpenMono.Config;
using OpenMono.Llm;

namespace OpenMono.Tests.Llm;

public class ProviderRegistryTests
{
    [Fact]
    public void ApplyProviderSettings_ActiveProviderUsesSameConnectionAsModelDiscovery()
    {
        var config = new AppConfig
        {
            Providers = new()
            {
                ["openai-compatible"] = new ProviderSettings
                {
                    Active = true,
                    Endpoint = "https://token-plan-sgp.xiaomimimo.com/v1",
                    Model = "mimo-v2.5-pro",
                    ApiKey = "test-key",
                },
            },
        };
        var registry = new ProviderRegistry();
        registry.ApplyProviderSettings(config);
        config.Llm.Provider.Should().Be("openai-compatible");
        config.Llm.Endpoint.Should().Be("https://token-plan-sgp.xiaomimimo.com/v1");
        config.Llm.Model.Should().Be("mimo-v2.5-pro");
        config.Llm.ApiKey.Should().Be("test-key");
        config.Llm.UsesLlamaExtensions.Should().BeFalse();
        using var client = registry.CreateClient(config);
        client.Should().BeOfType<OpenAiCompatClient>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ApplyProviderSettings_EmptyEnvironmentKeepsLocalDefault(string? providerOverride)
    {
        var config = new AppConfig();
        new ProviderRegistry().ApplyProviderSettings(config, providerOverride);
        config.Llm.UsesLlamaExtensions.Should().BeTrue();
        config.Llm.Endpoint.Should().Be("http://localhost:7474");
    }

    [Fact]
    public void ApplyProviderSettings_ExplicitProviderOverridesLegacyActiveProvider()
    {
        var config = new AppConfig
        {
            Llm = new() { Endpoint = "https://example.com/v1", Model = "custom-model" },
            Providers = new() { ["local"] = new() { Active = true } },
        };
        new ProviderRegistry().ApplyProviderSettings(config, "openai-compatible");
        config.Llm.UsesLlamaExtensions.Should().BeFalse();
        config.Llm.Model.Should().Be("custom-model");
        config.Llm.Endpoint.Should().Be("https://example.com/v1");
    }
}
