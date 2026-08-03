using System.Security.Cryptography;
using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.Retrieval;

public sealed class KnowledgeSearchService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IKnowledgeStore store,
    IOptions<RetrievalOptions> options) : IKnowledgeSearch
{
    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        RetrievalOptions value = options.Value;
        ReadOnlyMemory<float> vector = await embeddingGenerator.GenerateVectorAsync(query, cancellationToken: cancellationToken);
        return await store.SearchAsync(new(value.Collection, vector.ToArray(), value.TopK, value.MinimumSimilarity), cancellationToken);
    }
}

public sealed class KnowledgeIngestionService(
    DocumentExtractorRegistry extractors,
    DocumentChunker chunker,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IKnowledgeStore store,
    EmbeddingModelSelection embeddingSelection,
    IOptions<RetrievalOptions> options)
{
    public async Task<IngestionResult> IngestAsync(string path, string? sourceRoot = null, CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Document was not found.", fullPath);

        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string sourceId = CreateSourceId(fullPath, sourceRoot);
        RetrievalOptions value = options.Value;
        string embeddingIdentity = $"{embeddingSelection}/{value.EmbeddingDimensions}";
        string chunkingIdentity = $"chars:{value.ChunkSizeCharacters}:overlap:{value.ChunkOverlapCharacters}";

        StoredDocumentState? state = await store.GetDocumentStateAsync(value.Collection, sourceId, cancellationToken);
        if (state is not null && state.ContentHash == hash && state.EmbeddingIdentity == embeddingIdentity && state.ChunkingIdentity == chunkingIdentity)
        {
            return new(sourceId, 0, true, []);
        }

        ExtractedDocument extracted = await extractors.Resolve(fullPath).ExtractAsync(fullPath, cancellationToken);
        IReadOnlyList<DocumentChunk> drafts = chunker.Chunk(extracted);
        if (drafts.Count == 0) return new(sourceId, 0, false, extracted.Warnings);

        List<KnowledgeChunk> chunks = new(drafts.Count);
        foreach (DocumentChunk[] batch in drafts.Chunk(value.EmbeddingBatchSize))
        {
            GeneratedEmbeddings<Embedding<float>> generated = await embeddingGenerator.GenerateAsync(batch.Select(item => item.Text), cancellationToken: cancellationToken);
            if (generated.Count != batch.Length) throw new InvalidOperationException("Embedding provider returned an unexpected number of vectors.");
            for (int i = 0; i < batch.Length; i++)
            {
                float[] vector = generated[i].Vector.ToArray();
                if (vector.Length != value.EmbeddingDimensions) throw new InvalidOperationException($"Embedding dimension {vector.Length} does not match configured dimension {value.EmbeddingDimensions}.");
                chunks.Add(new(batch[i].Index, batch[i].Text, batch[i].PageNumber, batch[i].SectionName, vector));
            }
        }

        KnowledgeDocument document = new(value.Collection, sourceId, extracted.Title, fullPath, hash, embeddingIdentity, chunkingIdentity);
        await store.ReplaceDocumentAsync(document, chunks, cancellationToken);
        return new(sourceId, chunks.Count, false, extracted.Warnings);
    }

    private static string CreateSourceId(string fullPath, string? sourceRoot)
    {
        string value = sourceRoot is null ? Path.GetFileName(fullPath) : Path.GetRelativePath(Path.GetFullPath(sourceRoot), fullPath);
        return value.Replace(Path.DirectorySeparatorChar, '/');
    }
}

public sealed record IngestionResult(string SourceId, int Chunks, bool Skipped, IReadOnlyList<string> Warnings);
