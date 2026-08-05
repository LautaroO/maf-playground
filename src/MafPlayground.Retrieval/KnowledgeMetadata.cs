using System.Collections.ObjectModel;

namespace MafPlayground.Retrieval;

public sealed class KnowledgeMetadata : IEquatable<KnowledgeMetadata>
{
    public const int MaximumEntries = 32;
    public const int MaximumKeyLength = 100;
    public const int MaximumValueLength = 1000;

    public static KnowledgeMetadata Empty { get; } = new(
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)));

    private KnowledgeMetadata(IReadOnlyDictionary<string, string> values)
    {
        Values = values;
    }

    public IReadOnlyDictionary<string, string> Values { get; }

    public int Count => Values.Count;

    public static KnowledgeMetadata Create(
        IEnumerable<KeyValuePair<string, string>>? values)
    {
        if (values is null)
        {
            return Empty;
        }

        SortedDictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach ((string rawKey, string rawValue) in values)
        {
            string key = NormalizeKey(rawKey);
            string value = NormalizeValue(rawValue, key);
            if (!normalized.TryAdd(key, value))
            {
                throw new ArgumentException(
                    $"Metadata key '{key}' is specified more than once.",
                    nameof(values));
            }

            if (normalized.Count > MaximumEntries)
            {
                throw new ArgumentException(
                    $"Metadata supports at most {MaximumEntries} entries.",
                    nameof(values));
            }
        }

        return normalized.Count == 0
            ? Empty
            : new KnowledgeMetadata(
                new ReadOnlyDictionary<string, string>(normalized));
    }

    public bool Equals(KnowledgeMetadata? other) =>
        other is not null &&
        Values.Count == other.Values.Count &&
        Values.All(pair =>
            other.Values.TryGetValue(pair.Key, out string? value) &&
            string.Equals(pair.Value, value, StringComparison.Ordinal));

    public override bool Equals(object? obj) =>
        obj is KnowledgeMetadata other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach ((string key, string value) in Values)
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static string NormalizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        string normalized = key.Trim().ToLowerInvariant();
        if (normalized.Length > MaximumKeyLength)
        {
            throw new ArgumentException(
                $"Metadata keys cannot exceed {MaximumKeyLength} characters.",
                nameof(key));
        }

        return normalized;
    }

    private static string NormalizeValue(string value, string key)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                $"Metadata value for '{key}' cannot be empty.",
                nameof(value));
        }

        if (normalized.Length > MaximumValueLength)
        {
            throw new ArgumentException(
                $"Metadata values cannot exceed {MaximumValueLength} characters.",
                nameof(value));
        }

        return normalized;
    }
}
