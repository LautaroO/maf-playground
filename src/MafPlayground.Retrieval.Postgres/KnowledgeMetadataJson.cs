using System.Text.Json;
using MafPlayground.Retrieval;

namespace MafPlayground.Retrieval.Postgres;

internal static class KnowledgeMetadataJson
{
    public static string Serialize(KnowledgeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return JsonSerializer.Serialize(metadata.Values);
    }

    public static KnowledgeMetadata Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Dictionary<string, string>? values = JsonSerializer.Deserialize<
            Dictionary<string, string>>(json);
        return KnowledgeMetadata.Create(values);
    }
}
