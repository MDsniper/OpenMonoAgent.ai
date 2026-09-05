namespace OpenMono.Llm;

internal static class OpenAiEndpoint
{
    // A bare server URL gets /v1. Explicit API prefixes (e.g. /v1 or /api/v1)
    // are already base URLs, matching the OpenAI SDK's base_url convention.
    public static string Resource(string endpoint, string resource)
    {
        var uri = new Uri(endpoint.TrimEnd('/'), UriKind.Absolute);
        if (uri.Scheme is not ("http" or "https") || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("LLM endpoint must be an HTTP(S) API base URL without a query or fragment.", nameof(endpoint));
        var baseUrl = uri.AbsoluteUri.TrimEnd('/');
        return $"{baseUrl}{(uri.AbsolutePath == "/" ? "/v1" : "")}/{resource}";
    }
}
