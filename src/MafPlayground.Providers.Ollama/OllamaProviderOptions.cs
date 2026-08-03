namespace MafPlayground.Providers.Ollama;

public sealed class OllamaProviderOptions
{
    public const string ConfigurationSectionName = "AI:Providers:Ollama";

    public Uri Endpoint { get; init; } = new("http://localhost:11434");

    public OllamaPricingOptions Pricing { get; init; } = new();

    internal bool HasValidEndpoint() =>
        Endpoint.IsAbsoluteUri &&
        (Endpoint.Scheme == Uri.UriSchemeHttp || Endpoint.Scheme == Uri.UriSchemeHttps);

    internal bool HasValidPricing()
    {
        if (Pricing.Models.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(Pricing.Currency) ||
            string.IsNullOrWhiteSpace(Pricing.Version))
        {
            return false;
        }

        HashSet<string> models = new(StringComparer.OrdinalIgnoreCase);
        return Pricing.Models.All(model =>
            !string.IsNullOrWhiteSpace(model.Model) &&
            model.InputPerMillionTokens >= 0 &&
            model.OutputPerMillionTokens >= 0 &&
            models.Add(model.Model));
    }
}

public sealed class OllamaPricingOptions
{
    public string Currency { get; init; } = "USD";

    public string Version { get; init; } = "unspecified";

    public List<OllamaModelPriceOptions> Models { get; init; } = [];
}

public sealed class OllamaModelPriceOptions
{
    public string Model { get; init; } = string.Empty;

    public decimal InputPerMillionTokens { get; init; }

    public decimal OutputPerMillionTokens { get; init; }
}
