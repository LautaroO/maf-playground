namespace MafPlayground.Retrieval;

public interface IKnowledgeStore
{
    Task<StoredDocumentState?> GetDocumentStateAsync(string collection, string sourceId, CancellationToken cancellationToken);
    Task ReplaceDocumentAsync(KnowledgeDocument document, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(KnowledgeSearchRequest request, CancellationToken cancellationToken);
}

public interface IKnowledgeSearch
{
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

public interface IRetrievalDatabaseInitializer
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}

public sealed record StoredDocumentState(string ContentHash, string EmbeddingIdentity, string ChunkingIdentity);
public sealed record KnowledgeDocument(string Collection, string SourceId, string Title, string Path, string ContentHash, string EmbeddingIdentity, string ChunkingIdentity);
public sealed record KnowledgeChunk(int Index, string Text, int? PageNumber, string? SectionName, float[] Embedding);
public sealed record KnowledgeSearchRequest(string Collection, float[] Embedding, int TopK, double MinimumSimilarity);
public sealed record KnowledgeSearchResult(string SourceId, string Title, string Text, int? PageNumber, string? SectionName, double Similarity)
{
    public string Citation => PageNumber is int page
        ? $"[{Title}, page {page}, source: {SourceId}]"
        : $"[{Title}, source: {SourceId}]";
}
