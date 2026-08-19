namespace MafPlayground.Retrieval;

public sealed class KnowledgeBaseCatalogOptions
{
    public const string ConfigurationSectionName = "AI:KnowledgeBases";

    public Dictionary<string, KnowledgeBaseOptions> KnowledgeBases { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class KnowledgeBaseOptions
{
    public string Collection { get; set; } = string.Empty;

    public string EmbeddingModel { get; set; } = string.Empty;

    public int EmbeddingDimensions { get; set; } = 768;

    public KnowledgeIngestionOptions Ingestion { get; set; } = new();
}

public sealed class KnowledgeIngestionOptions
{
    public string TokenizerEncoding { get; set; } = "cl100k_base";

    public int MaxTokensPerChunk { get; set; } = 400;

    public int OverlapTokens { get; set; } = 40;

    public int EmbeddingBatchSize { get; set; } = 16;

    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;

    public int MaxDocumentSections { get; set; } = 1_000;

    public int MaxExtractedCharacters { get; set; } = 2_000_000;
}

public sealed class KnowledgeSearchOptions
{
    public int TopK { get; set; } = 5;

    public double MinimumSimilarity { get; set; } = 0.65;

    public int MaximumQueryCharacters { get; set; } = 2_000;

    public IReadOnlyDictionary<string, string> MetadataFilters { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record KnowledgeIngestionSettings(int EmbeddingBatchSize)
{
    public string TokenizerEncoding { get; init; } = "cl100k_base";

    public int MaxTokensPerChunk { get; init; } = 400;

    public int OverlapTokens { get; init; } = 40;

    public long MaxFileBytes { get; init; } = 20 * 1024 * 1024;

    public int MaxDocumentSections { get; init; } = 1_000;

    public int MaxExtractedCharacters { get; init; } = 2_000_000;
}

public sealed record KnowledgeBaseId
{
    public KnowledgeBaseId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record ResolvedKnowledgeBase(
    KnowledgeBaseId Id,
    string Collection,
    EmbeddingModelSelection EmbeddingModel,
    int EmbeddingDimensions,
    KnowledgeIngestionSettings Ingestion)
{
    public string EmbeddingIdentity => $"{EmbeddingModel}/{EmbeddingDimensions}";

}

public sealed class KnowledgeBaseCatalog
{
    private readonly IReadOnlyDictionary<string, ResolvedKnowledgeBase> _knowledgeBases;

    public KnowledgeBaseCatalog(KnowledgeBaseCatalogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.KnowledgeBases.Count == 0)
        {
            throw new KnowledgeBaseConfigurationException(
                $"At least one knowledge base must be configured under '{KnowledgeBaseCatalogOptions.ConfigurationSectionName}'.");
        }

        Dictionary<string, ResolvedKnowledgeBase> resolved =
            new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> collectionEmbeddingIdentities =
            new(StringComparer.OrdinalIgnoreCase);

        foreach ((string name, KnowledgeBaseOptions definition) in options.KnowledgeBases)
        {
            ResolvedKnowledgeBase knowledgeBase = Resolve(name, definition);
            if (!resolved.TryAdd(knowledgeBase.Id.Value, knowledgeBase))
            {
                throw new KnowledgeBaseConfigurationException(
                    $"Knowledge base '{knowledgeBase.Id}' is configured more than once.");
            }

            if (collectionEmbeddingIdentities.TryGetValue(
                    knowledgeBase.Collection,
                    out string? existingIdentity) &&
                !string.Equals(
                    existingIdentity,
                    knowledgeBase.EmbeddingIdentity,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new KnowledgeBaseConfigurationException(
                    $"Collection '{knowledgeBase.Collection}' is assigned incompatible embedding identities " +
                    $"'{existingIdentity}' and '{knowledgeBase.EmbeddingIdentity}'.");
            }

            collectionEmbeddingIdentities[knowledgeBase.Collection] =
                knowledgeBase.EmbeddingIdentity;
        }

        _knowledgeBases = resolved;
    }

    public IReadOnlyCollection<ResolvedKnowledgeBase> All =>
        _knowledgeBases.Values.ToArray();

    public ResolvedKnowledgeBase GetRequired(KnowledgeBaseId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _knowledgeBases.TryGetValue(id.Value, out ResolvedKnowledgeBase? knowledgeBase)
            ? knowledgeBase
            : throw new KnowledgeBaseConfigurationException(
                $"Knowledge base '{id}' is not configured. Available knowledge bases: " +
                $"{string.Join(", ", _knowledgeBases.Keys.Order(StringComparer.OrdinalIgnoreCase))}.");
    }

    public void ValidateEmbeddingDimensions(int supportedDimensions, string storeName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(supportedDimensions);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);

        ResolvedKnowledgeBase? incompatible = _knowledgeBases.Values.FirstOrDefault(
            knowledgeBase => knowledgeBase.EmbeddingDimensions != supportedDimensions);
        if (incompatible is not null)
        {
            throw new KnowledgeBaseConfigurationException(
                $"Knowledge base '{incompatible.Id}' uses {incompatible.EmbeddingDimensions} embedding dimensions, " +
                $"but {storeName} supports {supportedDimensions}.");
        }
    }

    private static ResolvedKnowledgeBase Resolve(
        string name,
        KnowledgeBaseOptions definition)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new KnowledgeBaseConfigurationException(
                "Knowledge-base names cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Collection))
        {
            throw new KnowledgeBaseConfigurationException(
                $"Knowledge base '{name}' requires a collection.");
        }

        if (!EmbeddingModelSelection.TryParse(
                definition.EmbeddingModel,
                out EmbeddingModelSelection? embeddingModel))
        {
            throw new KnowledgeBaseConfigurationException(
                $"Knowledge base '{name}' requires EmbeddingModel in provider:model format.");
        }

        if (definition.EmbeddingDimensions <= 0)
        {
            throw new KnowledgeBaseConfigurationException(
                $"Knowledge base '{name}' requires positive embedding dimensions.");
        }

        KnowledgeIngestionOptions ingestion = definition.Ingestion ?? new();
        if (string.IsNullOrWhiteSpace(ingestion.TokenizerEncoding))
        {
            throw new KnowledgeBaseConfigurationException(
                $"Knowledge base '{name}' requires a tokenizer encoding.");
        }

        if (ingestion.MaxTokensPerChunk <= 0)
        {
            throw new KnowledgeBaseConfigurationException(
                $"Knowledge base '{name}' requires a positive token chunk size.");
        }

        if (ingestion.OverlapTokens < 0 ||
            ingestion.OverlapTokens >= ingestion.MaxTokensPerChunk)
        {
            throw new KnowledgeBaseConfigurationException(
                $"Knowledge base '{name}' requires token overlap greater than or equal to zero and smaller than token chunk size.");
        }

        if (ingestion.EmbeddingBatchSize <= 0)
        {
            throw new KnowledgeBaseConfigurationException(
                $"Knowledge base '{name}' requires a positive embedding batch size.");
        }

        if (ingestion.MaxFileBytes <= 0 ||
            ingestion.MaxDocumentSections <= 0 ||
            ingestion.MaxExtractedCharacters <= 0)
        {
            throw new KnowledgeBaseConfigurationException(
                $"Knowledge base '{name}' requires positive ingestion resource limits.");
        }

        return new ResolvedKnowledgeBase(
            new KnowledgeBaseId(name),
            definition.Collection.Trim(),
            embeddingModel!,
            definition.EmbeddingDimensions,
            new KnowledgeIngestionSettings(ingestion.EmbeddingBatchSize)
            {
                TokenizerEncoding = ingestion.TokenizerEncoding.Trim(),
                MaxTokensPerChunk = ingestion.MaxTokensPerChunk,
                OverlapTokens = ingestion.OverlapTokens,
                MaxFileBytes = ingestion.MaxFileBytes,
                MaxDocumentSections = ingestion.MaxDocumentSections,
                MaxExtractedCharacters = ingestion.MaxExtractedCharacters,
            });
    }
}

public sealed class KnowledgeBaseConfigurationException(string message)
    : InvalidOperationException(message);
