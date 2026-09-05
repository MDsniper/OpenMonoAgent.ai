using OpenMono.Config;

namespace OpenMono.Llm;

public sealed class ProviderRegistry
{
    private readonly Dictionary<string, IProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public ProviderRegistry()
    {

        Register(new LocalLlamaProvider());
        Register(new OpenAiProvider());
        Register(new OpenAiCompatibleProvider());
        Register(new AnthropicProvider());
        Register(new OllamaProvider());
    }

    public void Register(IProvider provider) => _providers[provider.Name] = provider;

    public IProvider? Resolve(string name) => _providers.GetValueOrDefault(name);

    public IReadOnlyCollection<IProvider> All => _providers.Values;

    public ILlmClient CreateClient(AppConfig config)
    {
        if (config.Llm.Provider is null)
            ApplyProviderSettings(config);
        var name = (config.Llm.Provider ?? "local").ToLowerInvariant();
        var provider = Resolve(name)
            ?? throw new ArgumentException($"Unknown LLM provider '{name}'.");
        if (name is "local" or "openai" or "openai-compatible" or "ollama")
            return new OpenAiCompatClient(config.Llm) { ApiKey = config.Llm.ApiKey };

        return provider.CreateClient(new ProviderConfig
        {
            Name = name, Endpoint = config.Llm.Endpoint,
            ApiKey = config.Llm.ApiKey, Model = config.Llm.Model,
        });
    }

    // Resolve once, before environment/CLI overrides and startup model discovery, so
    // requests, the model picker, and context accounting all use the same backend.
    public void ApplyProviderSettings(AppConfig config, string? providerOverride = null)
    {
        if (string.IsNullOrWhiteSpace(providerOverride)) providerOverride = null;
        var active = config.Providers.FirstOrDefault(p => p.Value.Active);
        var name = (providerOverride ?? config.Llm.Provider ?? active.Key ?? "local").ToLowerInvariant();
        if (Resolve(name) is null)
            throw new ArgumentException($"Unknown LLM provider '{name}'.");

        config.Llm.Provider = name;
        var settings = config.Providers.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        var defaultEndpoint = name switch
        {
            "openai" => "https://api.openai.com/v1",
            "anthropic" => "https://api.anthropic.com",
            "ollama" => "http://localhost:11434/v1",
            _ => config.Llm.Endpoint,
        };
        config.Llm.Endpoint = settings?.Endpoint ??
            (config.Llm.Endpoint == "http://localhost:7474" ? defaultEndpoint : config.Llm.Endpoint);
        config.Llm.Model = settings?.Model ?? config.Llm.Model;
        config.Llm.ApiKey = settings?.ApiKey ?? config.Llm.ApiKey ?? (name switch
        {
            "openai" => Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            "anthropic" => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
            _ => null,
        });
        if (string.IsNullOrEmpty(config.Llm.Model) && name == "openai")
            config.Llm.Model = "gpt-4o";
    }

    public IReadOnlyList<string> ListModels()
    {
        return _providers.Values
            .SelectMany(p => p.SupportedModels.Select(m => $"{p.Name}/{m}"))
            .ToList();
    }
}

internal sealed class LocalLlamaProvider : IProvider
{
    public string Name => "local";
    public string[] SupportedModels => ["any-gguf-model"];

    public ILlmClient CreateClient(ProviderConfig config) =>
        new OpenAiCompatClient(new LlmConfig
        {
            Endpoint = config.Endpoint ?? "http://localhost:7474",
            Model = config.Model ?? "",
        }) { ApiKey = config.ApiKey };

    public bool ValidateConfig(ProviderConfig config, out string? error)
    {
        error = null;
        return true;
    }
}

internal sealed class OpenAiProvider : IProvider
{
    public string Name => "openai";
    public string[] SupportedModels => ["gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "o1", "o3-mini"];

    public ILlmClient CreateClient(ProviderConfig config)
    {
        var apiKey = config.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return new OpenAiCompatClient(new LlmConfig
        {
            Provider = Name,
            Endpoint = config.Endpoint ?? "https://api.openai.com",
            Model = config.Model ?? "gpt-4o",
        })
        { ApiKey = apiKey };
    }

    public bool ValidateConfig(ProviderConfig config, out string? error)
    {
        var key = config.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(key))
        {
            error = "OpenAI API key required. Set OPENAI_API_KEY or configure in settings.";
            return false;
        }
        error = null;
        return true;
    }
}

internal sealed class OllamaProvider : IProvider
{
    public string Name => "ollama";
    public string[] SupportedModels => ["llama3", "codellama", "qwen2.5-coder", "deepseek-coder-v2"];

    public ILlmClient CreateClient(ProviderConfig config) =>
        new OpenAiCompatClient(new LlmConfig
        {
            Provider = Name,
            Endpoint = config.Endpoint ?? "http://localhost:11434",
            Model = config.Model ?? "qwen2.5-coder",
        });

    public bool ValidateConfig(ProviderConfig config, out string? error)
    {
        error = null;
        return true;
    }
}

internal sealed class OpenAiCompatibleProvider : IProvider
{
    public string Name => "openai-compatible";
    public string[] SupportedModels => [];

    public ILlmClient CreateClient(ProviderConfig config) =>
        new OpenAiCompatClient(new LlmConfig
        {
            Provider = Name,
            Endpoint = config.Endpoint ?? throw new ArgumentException("An OpenAI-compatible API base URL is required."),
            Model = config.Model ?? "",
        }) { ApiKey = config.ApiKey };

    public bool ValidateConfig(ProviderConfig config, out string? error)
    {
        error = Uri.TryCreate(config.Endpoint, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? null : "Set an HTTP(S) API base URL for the OpenAI-compatible provider.";
        return error is null;
    }
}
