namespace MafPlayground.Providers.Google;

public sealed class GoogleGenAIProviderOptions
{
    public const string ConfigurationSectionName = "AI:Providers:Google";

    public string? ApiKey { get; init; }

    internal string? GetApiKey() =>
        string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey;
}
