namespace MafPlayground.Retrieval;

public sealed record EmbeddingModelSelection(string Provider, string Model)
{
    public static bool TryParse(string? value, out EmbeddingModelSelection? selection)
    {
        selection = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        int separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1) return false;

        string provider = value[..separator].Trim();
        string model = value[(separator + 1)..].Trim();
        if (provider.Length == 0 || model.Length == 0) return false;

        selection = new(provider, model);
        return true;
    }

    public override string ToString() => $"{Provider}:{Model}";
}
