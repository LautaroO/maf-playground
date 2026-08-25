using Microsoft.Extensions.AI;

namespace MafPlayground.Retrieval.Documents;

public interface IDocumentChunker
{
    ValueTask<IReadOnlyList<DocumentChunk>> ChunkAsync(
        ExtractedDocument document,
        KnowledgeIngestionSettings options,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        CancellationToken cancellationToken = default);

    string GetIdentity(KnowledgeIngestionSettings options);
}

public sealed record DocumentChunk(
    int Index,
    string Text,
    int? PageNumber,
    string? SectionName);
