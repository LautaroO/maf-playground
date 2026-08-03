namespace MafPlayground.Providers.Ollama;

public sealed class OllamaProviderOptions
{
    public const string ConfigurationSectionName = "AI:Providers:Ollama";

    public Uri Endpoint { get; init; } = new("http://localhost:11434");

    internal bool HasValidEndpoint() =>
        Endpoint.IsAbsoluteUri &&
        (Endpoint.Scheme == Uri.UriSchemeHttp || Endpoint.Scheme == Uri.UriSchemeHttps);
}
