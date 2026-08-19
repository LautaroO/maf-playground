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
                MaximumQueryCharacters = searchOptions.MaximumQueryCharacters,
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


        if (options.MaximumQueryCharacters <= 0)
        {
            throw new KnowledgeBaseConfigurationException(
                "Knowledge search requires MaximumQueryCharacters greater than zero.");
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
        if (query.Length > searchOptions.MaximumQueryCharacters)
        {
            throw new ArgumentException(
                $"Knowledge search query cannot exceed {searchOptions.MaximumQueryCharacters} characters.",
                nameof(query));
        }
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
    IDocumentChunker chunker,
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
        ValidateSourcePath(fullPath, sourceRoot);
        FileInfo file = new(fullPath);
        if (file.Length > knowledgeBase.Ingestion.MaxFileBytes)
        {
            throw new DocumentResourceLimitException(
                $"Document size {file.Length} bytes exceeds the configured maximum of " +
                $"{knowledgeBase.Ingestion.MaxFileBytes} bytes.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string sourceId = CreateSourceId(fullPath, sourceRoot);

        EmbeddingTokenizer tokenizer = runtime.ResolveTokenizer(knowledgeBase);
        string chunkingIdentity = chunker.GetIdentity(
            knowledgeBase.Ingestion,
            tokenizer);
        StoredDocumentState? state = await store.GetDocumentStateAsync(
            knowledgeBase.Collection,
            sourceId,
            cancellationToken);
        if (state is not null &&
            state.ContentHash == hash &&
            state.EmbeddingIdentity == knowledgeBase.EmbeddingIdentity &&
            state.ChunkingIdentity == chunkingIdentity &&
            state.Metadata.Equals(metadata))
        {
            return new(knowledgeBase.Id, sourceId, 0, true, []);
        }

        ExtractedDocument extracted = await extractors.Resolve(fullPath)
            .ExtractAsync(fullPath, cancellationToken);
        if (extracted.Sections.Count > knowledgeBase.Ingestion.MaxDocumentSections)
        {
            throw new DocumentResourceLimitException(
                $"Document contains {extracted.Sections.Count} sections; the configured maximum is " +
                $"{knowledgeBase.Ingestion.MaxDocumentSections}.");
        }

        long extractedCharacters = extracted.Sections.Sum(
            section => (long)section.GetMarkdown().Length);
        if (extractedCharacters > knowledgeBase.Ingestion.MaxExtractedCharacters)
        {
            throw new DocumentResourceLimitException(
                $"Extracted document text contains {extractedCharacters} characters; the configured maximum is " +
                $"{knowledgeBase.Ingestion.MaxExtractedCharacters}.");
        }
        IReadOnlyList<DocumentChunk> drafts = await chunker.ChunkAsync(
            extracted,
            knowledgeBase.Ingestion,
            tokenizer,
            cancellationToken);
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
            chunkingIdentity,
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

    private static void ValidateSourcePath(string fullPath, string? sourceRoot)
    {
        if (sourceRoot is null)
        {
            return;
        }

        string rootPath = Path.GetFullPath(sourceRoot);
        string relativePath = Path.GetRelativePath(rootPath, fullPath);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The document must be located below the configured source root.");
        }

        string resolvedRoot = new DirectoryInfo(rootPath)
            .ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? rootPath;
        string resolvedFile = new FileInfo(fullPath)
            .ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullPath;
        string resolvedRelative = Path.GetRelativePath(resolvedRoot, resolvedFile);
        if (Path.IsPathRooted(resolvedRelative) ||
            resolvedRelative.Equals("..", StringComparison.Ordinal) ||
            resolvedRelative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The document symbolic-link target must remain below the configured source root.");
        }
    }
}

public sealed class DocumentResourceLimitException(string message)
    : InvalidOperationException(message);

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
    private readonly ConcurrentDictionary<string, Lazy<EmbeddingTokenizer>>
        _tokenizers = new(StringComparer.OrdinalIgnoreCase);
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

    internal EmbeddingTokenizer ResolveTokenizer(ResolvedKnowledgeBase knowledgeBase)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(knowledgeBase);
        return _tokenizers.GetOrAdd(
            knowledgeBase.EmbeddingModel.ToString(),
            _ => new Lazy<EmbeddingTokenizer>(
                () => embeddingProviders.CreateTokenizer(knowledgeBase.EmbeddingModel),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
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
        _tokenizers.Clear();
        _disposed = true;
    }
}

internal sealed record KnowledgeBaseRuntimeSelection(
    ResolvedKnowledgeBase KnowledgeBase,
    IEmbeddingGenerator<string, Embedding<float>> EmbeddingGenerator);
