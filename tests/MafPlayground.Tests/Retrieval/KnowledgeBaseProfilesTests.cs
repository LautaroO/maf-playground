using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests.Retrieval;

public sealed class KnowledgeBaseProfilesTests
{
    [Fact]
    public void Catalog_RejectsSharedCollectionWithDifferentEmbeddingIdentity()
    {
        KnowledgeBaseCatalogOptions options = new()
        {
            KnowledgeBases = new Dictionary<string, KnowledgeBaseOptions>
            {
                ["Help"] = CreateKnowledgeBase("shared", "fake:help"),
                ["Legal"] = CreateKnowledgeBase("shared", "fake:legal"),
            },
        };

        KnowledgeBaseConfigurationException exception = Assert.Throws<
            KnowledgeBaseConfigurationException>(() => new KnowledgeBaseCatalog(options));

        Assert.Contains("incompatible embedding identities", exception.Message);
    }

    [Fact]
    public void Catalog_RejectsInvalidChunkOverlap()
    {
        KnowledgeBaseOptions definition = CreateKnowledgeBase("help", "fake:model");
        definition.Ingestion.ChunkSizeCharacters = 100;
        definition.Ingestion.ChunkOverlapCharacters = 100;

        Assert.Throws<KnowledgeBaseConfigurationException>(() =>
            new KnowledgeBaseCatalog(new KnowledgeBaseCatalogOptions
            {
                KnowledgeBases = new Dictionary<string, KnowledgeBaseOptions>
                {
                    ["Help"] = definition,
                },
            }));
    }

    [Fact]
    public void Catalog_RejectsKnowledgeBaseIncompatibleWithStoreDimensions()
    {
        KnowledgeBaseCatalog catalog = new(new KnowledgeBaseCatalogOptions
        {
            KnowledgeBases = new Dictionary<string, KnowledgeBaseOptions>
            {
                ["Help"] = CreateKnowledgeBase("help", "fake:model"),
            },
        });

        KnowledgeBaseConfigurationException exception = Assert.Throws<
            KnowledgeBaseConfigurationException>(() =>
            catalog.ValidateEmbeddingDimensions(768, "test store"));

        Assert.Contains("Help", exception.Message);
        Assert.Contains("test store supports 768", exception.Message);
    }

    [Fact]
    public async Task SearchFactory_IsolatesCollectionModelAndPolicyByKnowledgeBase()
    {
        KnowledgeBaseCatalog catalog = new(new KnowledgeBaseCatalogOptions
        {
            KnowledgeBases = new Dictionary<string, KnowledgeBaseOptions>
            {
                ["Help"] = CreateKnowledgeBase("help-collection", "fake:help-model"),
                ["Legal"] = CreateKnowledgeBase("legal-collection", "fake:legal-model"),
            },
        });
        RecordingEmbeddingProvider embeddingProvider = new();
        RecordingKnowledgeStore store = new();
        EmbeddingProviderRegistry providers = new([embeddingProvider]);
        using KnowledgeBaseRuntime runtime = new(catalog, providers);
        KnowledgeSearchFactory factory = new(runtime, store);

        IKnowledgeSearch help = factory.Create(
            new KnowledgeBaseId("Help"),
            new KnowledgeSearchOptions
            {
                TopK = 3,
                MinimumSimilarity = 0.6,
                MetadataFilters = new Dictionary<string, string>
                {
                    ["Audience"] = "customer",
                },
            });
        IKnowledgeSearch legal = factory.Create(
            new KnowledgeBaseId("Legal"),
            new KnowledgeSearchOptions { TopK = 8, MinimumSimilarity = 0.8 });

        await help.SearchAsync("reset password");
        await legal.SearchAsync("retention policy");

        Assert.Equal(["help-model", "legal-model"], embeddingProvider.CreatedModels);
        Assert.Collection(
            store.Requests,
            request =>
            {
                Assert.Equal("help-collection", request.Collection);
                Assert.Equal(3, request.TopK);
                Assert.Equal(0.6, request.MinimumSimilarity);
                Assert.Equal("customer", request.MetadataFilters.Values["audience"]);
            },
            request =>
            {
                Assert.Equal("legal-collection", request.Collection);
                Assert.Equal(8, request.TopK);
                Assert.Equal(0.8, request.MinimumSimilarity);
            });
    }

    [Fact]
    public void SearchFactory_RejectsUnknownKnowledgeBase()
    {
        KnowledgeBaseCatalog catalog = new(new KnowledgeBaseCatalogOptions
        {
            KnowledgeBases = new Dictionary<string, KnowledgeBaseOptions>
            {
                ["Help"] = CreateKnowledgeBase("help", "fake:model"),
            },
        });
        using KnowledgeBaseRuntime runtime = new(
            catalog,
            new EmbeddingProviderRegistry([new RecordingEmbeddingProvider()]));
        KnowledgeSearchFactory factory = new(runtime, new RecordingKnowledgeStore());

        KnowledgeBaseConfigurationException exception = Assert.Throws<
            KnowledgeBaseConfigurationException>(() => factory.Create(
                new KnowledgeBaseId("Missing"),
                new KnowledgeSearchOptions()));

        Assert.Contains("Available knowledge bases: Help", exception.Message);
    }

    [Fact]
    public async Task Ingestion_UsesSelectedKnowledgeBaseIdentityAndCollection()
    {
        KnowledgeBaseCatalog catalog = new(new KnowledgeBaseCatalogOptions
        {
            KnowledgeBases = new Dictionary<string, KnowledgeBaseOptions>
            {
                ["Help"] = CreateKnowledgeBase("help-collection", "fake:help-model"),
                ["Legal"] = CreateKnowledgeBase("legal-collection", "fake:legal-model"),
            },
        });
        RecordingKnowledgeStore store = new();
        using KnowledgeBaseRuntime runtime = new(
            catalog,
            new EmbeddingProviderRegistry([new RecordingEmbeddingProvider()]));
        KnowledgeIngestionService ingestion = new(
            new DocumentExtractorRegistry([new FakeDocumentExtractor()]),
            new DocumentChunker(),
            runtime,
            store);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"maf-profile-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "test");

        try
        {
            IngestionResult result = await ingestion.IngestAsync(
                new KnowledgeBaseId("Legal"),
                path);

            Assert.Equal("Legal", result.KnowledgeBase.Value);
            Assert.NotNull(store.ReplacedDocument);
            Assert.Equal("legal-collection", store.ReplacedDocument.Collection);
            Assert.Equal("fake:legal-model/3", store.ReplacedDocument.EmbeddingIdentity);
            Assert.Equal("chars:100:overlap:10", store.ReplacedDocument.ChunkingIdentity);
            Assert.Equal(KnowledgeMetadata.Empty, store.ReplacedDocument.Metadata);
            Assert.NotEmpty(store.ReplacedChunks);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Ingestion_ReplacesDocumentWhenOnlyMetadataChanges()
    {
        KnowledgeBaseCatalog catalog = new(new KnowledgeBaseCatalogOptions
        {
            KnowledgeBases = new Dictionary<string, KnowledgeBaseOptions>
            {
                ["Help"] = CreateKnowledgeBase("help", "fake:model"),
            },
        });
        RecordingKnowledgeStore store = new();
        using KnowledgeBaseRuntime runtime = new(
            catalog,
            new EmbeddingProviderRegistry([new RecordingEmbeddingProvider()]));
        KnowledgeIngestionService ingestion = new(
            new DocumentExtractorRegistry([new FakeDocumentExtractor()]),
            new DocumentChunker(),
            runtime,
            store);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"maf-metadata-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "test");

        try
        {
            KnowledgeMetadata original = KnowledgeMetadata.Create(
                new Dictionary<string, string> { ["audience"] = "customer" });
            await ingestion.IngestAsync(
                new KnowledgeBaseId("Help"), path, null, original);
            KnowledgeDocument first = Assert.IsType<KnowledgeDocument>(
                store.ReplacedDocument);
            store.DocumentState = new StoredDocumentState(
                first.ContentHash,
                first.EmbeddingIdentity,
                first.ChunkingIdentity,
                original);

            KnowledgeMetadata updated = KnowledgeMetadata.Create(
                new Dictionary<string, string> { ["audience"] = "internal" });
            IngestionResult result = await ingestion.IngestAsync(
                new KnowledgeBaseId("Help"), path, null, updated);

            Assert.False(result.Skipped);
            Assert.Equal(2, store.ReplaceCount);
            Assert.Equal(updated, store.ReplacedDocument!.Metadata);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static KnowledgeBaseOptions CreateKnowledgeBase(
        string collection,
        string embeddingModel) => new()
        {
            Collection = collection,
            EmbeddingModel = embeddingModel,
            EmbeddingDimensions = 3,
            Ingestion = new KnowledgeIngestionOptions
            {
                ChunkSizeCharacters = 100,
                ChunkOverlapCharacters = 10,
                EmbeddingBatchSize = 4,
            },
        };

    private sealed class RecordingEmbeddingProvider : IEmbeddingGeneratorProvider
    {
        public string Name => "fake";

        public List<string> CreatedModels { get; } = [];

        public IEmbeddingGenerator<string, Embedding<float>> Create(string model)
        {
            CreatedModels.Add(model);
            return new FakeEmbeddingGenerator();
        }
    }

    private sealed class FakeEmbeddingGenerator
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            GeneratedEmbeddings<Embedding<float>> embeddings = new(
                values.Select(_ => new Embedding<float>(new float[] { 1, 0, 0 })));
            return Task.FromResult(embeddings);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingKnowledgeStore : IKnowledgeStore
    {
        public List<KnowledgeSearchRequest> Requests { get; } = [];

        public KnowledgeDocument? ReplacedDocument { get; private set; }

        public IReadOnlyList<KnowledgeChunk> ReplacedChunks { get; private set; } = [];

        public StoredDocumentState? DocumentState { get; set; }

        public int ReplaceCount { get; private set; }

        public Task<StoredDocumentState?> GetDocumentStateAsync(
            string collection,
            string sourceId,
            CancellationToken cancellationToken) => Task.FromResult(DocumentState);

        public Task ReplaceDocumentAsync(
            KnowledgeDocument document,
            IReadOnlyList<KnowledgeChunk> chunks,
            CancellationToken cancellationToken)
        {
            ReplaceCount++;
            ReplacedDocument = document;
            ReplacedChunks = chunks;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
            KnowledgeSearchRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult<IReadOnlyList<KnowledgeSearchResult>>([]);
        }
    }

    private sealed class FakeDocumentExtractor : IDocumentExtractor
    {
        public IReadOnlySet<string> SupportedExtensions { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" };

        public Task<ExtractedDocument> ExtractAsync(
            string path,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new ExtractedDocument(
                    "test",
                    [new ExtractedDocumentSection("A legal retention policy applies.")],
                    []));
    }
}
