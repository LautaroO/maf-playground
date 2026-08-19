using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests.Retrieval;

public sealed class KnowledgeBaseProfilesTests
{
    [Fact]
    public void Runtime_RejectsSharedCollectionWithDifferentProviderIdentity()
    {
        KnowledgeBaseCatalogOptions options = new()
        {
            KnowledgeBases = new Dictionary<string, KnowledgeBaseOptions>
            {
                ["Help"] = CreateKnowledgeBase("shared", "fake:help"),
                ["Legal"] = CreateKnowledgeBase("shared", "fake:legal"),
            },
        };

        KnowledgeBaseCatalog catalog = new(options);

        KnowledgeBaseConfigurationException exception = Assert.Throws<
            KnowledgeBaseConfigurationException>(() => new KnowledgeBaseRuntime(
                catalog,
                new EmbeddingProviderRegistry([new RecordingEmbeddingProvider()])));

        Assert.Contains("incompatible embedding identities", exception.Message);
    }

    [Fact]
    public void Catalog_RejectsInvalidTokenOverlap()
    {
        KnowledgeBaseOptions definition = CreateKnowledgeBase("help", "fake:model");
        definition.Ingestion.MaxTokensPerChunk = 100;
        definition.Ingestion.OverlapTokens = 100;

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
        Assert.All(embeddingProvider.CreatedGenerators, created =>
        {
            Assert.Equal(3, created.Dimensions);
            Assert.Equal(EmbeddingPurpose.Query, created.Purpose);
        });
        Assert.Empty(embeddingProvider.CreatedTokenizerModels);
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
        RecordingEmbeddingProvider embeddingProvider = new();
        using KnowledgeBaseRuntime runtime = new(
            catalog,
            new EmbeddingProviderRegistry([embeddingProvider]));
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
        RecordingEmbeddingProvider embeddingProvider = new();
        using KnowledgeBaseRuntime runtime = new(
            catalog,
            new EmbeddingProviderRegistry([embeddingProvider]));
        KnowledgeIngestionService ingestion = new(
            new DocumentExtractorRegistry([new FakeDocumentExtractor()]),
            new MicrosoftDataIngestionDocumentChunker(),
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
            Assert.Equal("fake:legal-model/3/raw-v1", store.ReplacedDocument.EmbeddingIdentity);
            Assert.Contains(
                "document-tokens-per-section:fake:cl100k_base:max:400:overlap:40",
                store.ReplacedDocument.ChunkingIdentity);
            Assert.Contains("legal-model", embeddingProvider.CreatedTokenizerModels);
            Assert.Contains(
                embeddingProvider.CreatedGenerators,
                created => created is
                {
                    Model: "legal-model",
                    Dimensions: 3,
                    Purpose: EmbeddingPurpose.Document,
                });
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
            new MicrosoftDataIngestionDocumentChunker(),
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

    [Fact]
    public async Task Ingestion_ReplacesDocumentWhenProviderStrategyIdentityChanges()
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
            new MicrosoftDataIngestionDocumentChunker(),
            runtime,
            store);
        string path = Path.Combine(
            Path.GetTempPath(),
            $"maf-identity-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "test");

        try
        {
            await ingestion.IngestAsync(new KnowledgeBaseId("Help"), path);
            KnowledgeDocument first = Assert.IsType<KnowledgeDocument>(
                store.ReplacedDocument);
            store.DocumentState = new StoredDocumentState(
                first.ContentHash,
                "fake:model/3",
                first.ChunkingIdentity,
                first.Metadata);

            IngestionResult result = await ingestion.IngestAsync(
                new KnowledgeBaseId("Help"),
                path);

            Assert.False(result.Skipped);
            Assert.Equal(2, store.ReplaceCount);
            Assert.Equal(
                "fake:model/3/raw-v1",
                store.ReplacedDocument!.EmbeddingIdentity);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Ingestion_RejectsDocumentOutsideSourceRoot()
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
        KnowledgeIngestionService ingestion = new(
            new DocumentExtractorRegistry([new FakeDocumentExtractor()]),
            new MicrosoftDataIngestionDocumentChunker(),
            runtime,
            new RecordingKnowledgeStore());
        string sourceRoot = Path.Combine(
            Path.GetTempPath(),
            $"maf-root-{Guid.NewGuid():N}");
        string outsidePath = Path.Combine(
            Path.GetTempPath(),
            $"maf-outside-{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(sourceRoot);
        await File.WriteAllTextAsync(outsidePath, "test");

        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await ingestion.IngestAsync(
                    new KnowledgeBaseId("Help"),
                    outsidePath,
                    sourceRoot));
        }
        finally
        {
            File.Delete(outsidePath);
            Directory.Delete(sourceRoot);
        }
    }

    [Fact]
    public async Task Ingestion_RejectsFileBeforeReadingWhenSizeLimitIsExceeded()
    {
        KnowledgeBaseOptions knowledgeBase = CreateKnowledgeBase("help", "fake:model");
        knowledgeBase.Ingestion.MaxFileBytes = 3;
        KnowledgeBaseCatalog catalog = new(new KnowledgeBaseCatalogOptions
        {
            KnowledgeBases = new Dictionary<string, KnowledgeBaseOptions>
            {
                ["Help"] = knowledgeBase,
            },
        });
        using KnowledgeBaseRuntime runtime = new(
            catalog,
            new EmbeddingProviderRegistry([new RecordingEmbeddingProvider()]));
        KnowledgeIngestionService ingestion = new(
            new DocumentExtractorRegistry([new FakeDocumentExtractor()]),
            new MicrosoftDataIngestionDocumentChunker(),
            runtime,
            new RecordingKnowledgeStore());
        string path = Path.Combine(
            Path.GetTempPath(),
            $"maf-large-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "test");

        try
        {
            await Assert.ThrowsAsync<DocumentResourceLimitException>(async () =>
                await ingestion.IngestAsync(new KnowledgeBaseId("Help"), path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Search_RejectsOversizedQueryBeforeGeneratingEmbedding()
    {
        KnowledgeBaseCatalog catalog = new(new KnowledgeBaseCatalogOptions
        {
            KnowledgeBases = new Dictionary<string, KnowledgeBaseOptions>
            {
                ["Help"] = CreateKnowledgeBase("help", "fake:model"),
            },
        });
        RecordingEmbeddingProvider embeddingProvider = new();
        using KnowledgeBaseRuntime runtime = new(
            catalog,
            new EmbeddingProviderRegistry([embeddingProvider]));
        IKnowledgeSearch search = new KnowledgeSearchFactory(
            runtime,
            new RecordingKnowledgeStore()).Create(
                new KnowledgeBaseId("Help"),
                new KnowledgeSearchOptions { MaximumQueryCharacters = 4 });

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await search.SearchAsync("12345"));
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
                EmbeddingBatchSize = 4,
            },
        };

    private sealed class RecordingEmbeddingProvider : IEmbeddingGeneratorProvider
    {
        public string Name => "fake";

        public string GetEmbeddingIdentity(string model, int dimensions) =>
            $"{Name}:{model}/{dimensions}/raw-v1";

        public List<string> CreatedModels { get; } = [];

        public List<CreatedEmbeddingGenerator> CreatedGenerators { get; } = [];

        public List<string> CreatedTokenizerModels { get; } = [];

        public IEmbeddingGenerator<string, Embedding<float>> Create(
            string model,
            int dimensions,
            EmbeddingPurpose purpose)
        {
            CreatedModels.Add(model);
            CreatedGenerators.Add(new(model, dimensions, purpose));
            return new FakeEmbeddingGenerator();
        }

        public ValueTask<EmbeddingTokenizer> CreateTokenizerAsync(
            string model,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreatedTokenizerModels.Add(model);
            return ValueTask.FromResult<EmbeddingTokenizer>(
                new LocalEmbeddingTokenizer(
                    Microsoft.ML.Tokenizers.TiktokenTokenizer.CreateForEncoding(
                        "cl100k_base"),
                    "fake:cl100k_base"));
        }
    }

    private sealed record CreatedEmbeddingGenerator(
        string Model,
        int Dimensions,
        EmbeddingPurpose Purpose);

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
                    CreateDocument(),
                    []));

        private static Microsoft.Extensions.DataIngestion.IngestionDocument CreateDocument()
        {
            Microsoft.Extensions.DataIngestion.IngestionDocument document = new("test");
            Microsoft.Extensions.DataIngestion.IngestionDocumentSection section = new();
            section.Elements.Add(
                new Microsoft.Extensions.DataIngestion.IngestionDocumentParagraph(
                    "A legal retention policy applies."));
            document.Sections.Add(section);
            return document;
        }
    }
}
