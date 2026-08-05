using System.Collections.Concurrent;
using System.Security.Cryptography;
using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.AI;

namespace MafPlayground.Retrieval;

public sealed class KnowledgeSearchFactory(
    KnowledgeBaseRuntime runtime,
    IKnowledgeStore store) : IKnowledgeSearchFactory
{
    public IKnowledgeSearch Create(
        KnowledgeBaseId knowledgeBaseId,
        KnowledgeSearchOptions searchOptions)
    {
        ArgumentNullException.ThrowIfNull(knowledgeBaseId);
        ArgumentNullException.ThrowIfNull(searchOptions);
        ValidateSearchOptions(searchOptions);
        KnowledgeMetadata metadataFilters = KnowledgeMetadata.Create(
            searchOptions.MetadataFilters);

        KnowledgeBaseRuntimeSelection selection = runtime.Resolve(knowledgeBaseId);
        return new KnowledgeSearchService(
            selection.EmbeddingGenerator,
            store,
            selection.KnowledgeBase,
            new KnowledgeSearchOptions
            {
                TopK = searchOptions.TopK,
                MinimumSimilarity = searchOptions.MinimumSimilarity,
                MetadataFilters = metadataFilters.Values,
            });
    }

    private static void ValidateSearchOptions(KnowledgeSearchOptions options)
    {
        if (options.TopK <= 0)
        {
            throw new KnowledgeBaseConfigurationException(
                "Knowledge search requires TopK greater than zero.");
        }

        if (options.MinimumSimilarity is < 0 or > 1)
        {
            throw new KnowledgeBaseConfigurationException(
                "Knowledge search requires MinimumSimilarity between zero and one.");
        }
    }
}

public sealed class KnowledgeSearchService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IKnowledgeStore store,
    ResolvedKnowledgeBase knowledgeBase,
    KnowledgeSearchOptions searchOptions) : IKnowledgeSearch
{
    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ReadOnlyMemory<float> vector = await embeddingGenerator.GenerateVectorAsync(
            query,
            cancellationToken: cancellationToken);
        if (vector.Length != knowledgeBase.EmbeddingDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding dimension {vector.Length} does not match knowledge base " +
                $"'{knowledgeBase.Id}' dimension {knowledgeBase.EmbeddingDimensions}.");
        }

        return await store.SearchAsync(
            new KnowledgeSearchRequest(
                knowledgeBase.Collection,
                vector.ToArray(),
                searchOptions.TopK,
                searchOptions.MinimumSimilarity,
                KnowledgeMetadata.Create(searchOptions.MetadataFilters)),
            cancellationToken);
    }
}

public sealed class KnowledgeIngestionService(
    DocumentExtractorRegistry extractors,
    DocumentChunker chunker,
    KnowledgeBaseRuntime runtime,
    IKnowledgeStore store)
{
    public async Task<IngestionResult> IngestAsync(
        KnowledgeBaseId knowledgeBaseId,
        string path,
        string? sourceRoot = null,
        CancellationToken cancellationToken = default)
        => await IngestAsync(
            knowledgeBaseId,
            path,
            sourceRoot,
            KnowledgeMetadata.Empty,
            cancellationToken);

    public async Task<IngestionResult> IngestAsync(
        KnowledgeBaseId knowledgeBaseId,
        string path,
        string? sourceRoot,
        KnowledgeMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(knowledgeBaseId);
        ArgumentNullException.ThrowIfNull(metadata);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Document was not found.", fullPath);
        }

        KnowledgeBaseRuntimeSelection selection = runtime.Resolve(knowledgeBaseId);
        ResolvedKnowledgeBase knowledgeBase = selection.KnowledgeBase;
        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string sourceId = CreateSourceId(fullPath, sourceRoot);

        StoredDocumentState? state = await store.GetDocumentStateAsync(
            knowledgeBase.Collection,
            sourceId,
            cancellationToken);
        if (state is not null &&
            state.ContentHash == hash &&
            state.EmbeddingIdentity == knowledgeBase.EmbeddingIdentity &&
            state.ChunkingIdentity == knowledgeBase.ChunkingIdentity &&
            state.Metadata.Equals(metadata))
        {
            return new(knowledgeBase.Id, sourceId, 0, true, []);
        }

        ExtractedDocument extracted = await extractors.Resolve(fullPath)
            .ExtractAsync(fullPath, cancellationToken);
        IReadOnlyList<DocumentChunk> drafts = chunker.Chunk(
            extracted,
            knowledgeBase.Ingestion);
        if (drafts.Count == 0)
        {
            return new(knowledgeBase.Id, sourceId, 0, false, extracted.Warnings);
        }

        List<KnowledgeChunk> chunks = new(drafts.Count);
        foreach (DocumentChunk[] batch in drafts.Chunk(
                     knowledgeBase.Ingestion.EmbeddingBatchSize))
        {
            GeneratedEmbeddings<Embedding<float>> generated =
                await selection.EmbeddingGenerator.GenerateAsync(
                    batch.Select(item => item.Text),
                    cancellationToken: cancellationToken);
            if (generated.Count != batch.Length)
            {
                throw new InvalidOperationException(
                    "Embedding provider returned an unexpected number of vectors.");
            }

            for (int i = 0; i < batch.Length; i++)
            {
                float[] vector = generated[i].Vector.ToArray();
                if (vector.Length != knowledgeBase.EmbeddingDimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding dimension {vector.Length} does not match knowledge base " +
                        $"'{knowledgeBase.Id}' dimension {knowledgeBase.EmbeddingDimensions}.");
                }

                chunks.Add(new KnowledgeChunk(
                    batch[i].Index,
                    batch[i].Text,
                    batch[i].PageNumber,
                    batch[i].SectionName,
                    vector));
            }
        }

        KnowledgeDocument document = new(
            knowledgeBase.Collection,
            sourceId,
            extracted.Title,
            fullPath,
            hash,
            knowledgeBase.EmbeddingIdentity,
            knowledgeBase.ChunkingIdentity,
            metadata);
        await store.ReplaceDocumentAsync(document, chunks, cancellationToken);
        return new(
            knowledgeBase.Id,
            sourceId,
            chunks.Count,
            false,
            extracted.Warnings);
    }

    private static string CreateSourceId(string fullPath, string? sourceRoot)
    {
        string value = sourceRoot is null
            ? Path.GetFileName(fullPath)
            : Path.GetRelativePath(Path.GetFullPath(sourceRoot), fullPath);
        return value.Replace(Path.DirectorySeparatorChar, '/');
    }
}

public sealed record IngestionResult(
    KnowledgeBaseId KnowledgeBase,
    string SourceId,
    int Chunks,
    bool Skipped,
    IReadOnlyList<string> Warnings);

public sealed class KnowledgeBaseRuntime(
    KnowledgeBaseCatalog catalog,
    EmbeddingProviderRegistry embeddingProviders) : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<IEmbeddingGenerator<string, Embedding<float>>>>
        _embeddingGenerators = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    internal KnowledgeBaseRuntimeSelection Resolve(KnowledgeBaseId knowledgeBaseId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ResolvedKnowledgeBase knowledgeBase = catalog.GetRequired(knowledgeBaseId);
        Lazy<IEmbeddingGenerator<string, Embedding<float>>> generator =
            _embeddingGenerators.GetOrAdd(
                knowledgeBase.EmbeddingModel.ToString(),
                _ => new Lazy<IEmbeddingGenerator<string, Embedding<float>>>(
                    () => embeddingProviders.Create(knowledgeBase.EmbeddingModel),
                    LazyThreadSafetyMode.ExecutionAndPublication));
        return new KnowledgeBaseRuntimeSelection(knowledgeBase, generator.Value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (Lazy<IEmbeddingGenerator<string, Embedding<float>>> generator in
                 _embeddingGenerators.Values)
        {
            if (generator.IsValueCreated)
            {
                generator.Value.Dispose();
            }
        }

        _embeddingGenerators.Clear();
        _disposed = true;
    }
}

internal sealed record KnowledgeBaseRuntimeSelection(
    ResolvedKnowledgeBase KnowledgeBase,
    IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator);
