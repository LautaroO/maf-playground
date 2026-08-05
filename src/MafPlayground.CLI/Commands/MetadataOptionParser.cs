using MafPlayground.Retrieval;

namespace MafPlayground.CLI.Commands;

public static class MetadataOptionParser
{
    public static KnowledgeMetadata Parse(
        IEnumerable<string>? values,
        string optionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionName);
        if (values is null)
        {
            return KnowledgeMetadata.Empty;
        }

        List<KeyValuePair<string, string>> entries = [];
        foreach (string value in values)
        {
            int separator = value.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == value.Length - 1)
            {
                throw new ArgumentException(
                    $"{optionName} values must use key=value format.",
                    nameof(values));
            }

            entries.Add(new KeyValuePair<string, string>(
                value[..separator],
                value[(separator + 1)..]));
        }

        return KnowledgeMetadata.Create(entries);
    }
}
