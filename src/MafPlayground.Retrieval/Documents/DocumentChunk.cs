namespace MafPlayground.Retrieval.Documents;

public interface IDocumentChunker
{
    ValueTask<IReadOnlyList<DocumentChunk>> ChunkAsync(
        ExtractedDocument document,
        KnowledgeIngestionSettings options,
        EmbeddingTokenizer tokenizer,
        CancellationToken cancellationToken = default);

    string GetIdentity(
        KnowledgeIngestionSettings options,
        EmbeddingTokenizer tokenizer);
}

public sealed record DocumentChunk(
    int Index,
    string Text,
    int? PageNumber,
    string? SectionName);
