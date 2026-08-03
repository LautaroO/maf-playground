namespace MafPlayground.Retrieval;

public sealed class RetrievalOptions
{
    public const string ConfigurationSectionName = "AI:Retrieval";

    public string Collection { get; set; } = "basic-rag";
    public int EmbeddingDimensions { get; set; } = 768;
    public int ChunkSizeCharacters { get; set; } = 1200;
    public int ChunkOverlapCharacters { get; set; } = 200;
    public int EmbeddingBatchSize { get; set; } = 16;
    public int TopK { get; set; } = 5;
    public double MinimumSimilarity { get; set; } = 0.65;
    public int MaximumAdditionalSearches { get; set; } = 1;
}
