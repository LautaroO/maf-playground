using System.Diagnostics.CodeAnalysis;

namespace MafPlayground.AI.Configuration;

public sealed record AIModelSelection
{
    private AIModelSelection(string provider, string model)
    {
        Provider = provider;
        Model = model;
    }

    public string Provider { get; }

    public string Model { get; }

    public static AIModelSelection Parse(string value)
    {
        if (!TryParse(value, out AIModelSelection? selection))
        {
            throw new FormatException(
                "The AI model must use the 'provider:model' format, for example 'ollama:llama3.1:8b'.");
        }

        return selection;
    }

    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out AIModelSelection? selection)
    {
        selection = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        int separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        string provider = value[..separatorIndex].Trim();
        string model = value[(separatorIndex + 1)..].Trim();
        if (!IsValidProviderName(provider) || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        selection = new AIModelSelection(provider.ToLowerInvariant(), model);
        return true;
    }

    public override string ToString() => $"{Provider}:{Model}";

    private static bool IsValidProviderName(string provider)
    {
        if (provider.Length == 0 || !char.IsLetterOrDigit(provider[0]))
        {
            return false;
        }

        return provider.All(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
    }
}
