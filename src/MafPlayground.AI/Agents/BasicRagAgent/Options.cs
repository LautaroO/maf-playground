using MafPlayground.AI.Guards;

namespace MafPlayground.AI.Agents.BasicRagAgent;

public sealed class BasicRagAgentOptions
{
    public const string ConfigurationSectionName = "AI:Agents:BasicRag";

    public string KnowledgeBase { get; set; } = "Help";

    public string GuardProfile { get; set; } = GuardProfileNames.Default;

    public RagRetrievalOptions Retrieval { get; set; } = new();
}

public sealed class RagRetrievalOptions
{
    public int TopK { get; set; } = 5;

    public double MinimumSimilarity { get; set; } = 0.65;

    public int MaximumAdditionalSearches { get; set; } = 1;

    public int MaximumQueryCharacters { get; set; } = 2_000;

    public Dictionary<string, string> MetadataFilters { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
