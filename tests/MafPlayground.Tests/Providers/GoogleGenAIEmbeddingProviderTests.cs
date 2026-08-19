using Google.GenAI;
using Google.GenAI.Types;
using MafPlayground.Providers.Google;
using MafPlayground.Retrieval;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests.Providers;

public sealed class GoogleGenAIEmbeddingProviderTests
{
    [Fact]
    public async Task RemoteTokenizer_ShrinksCandidateToExactTokenLimit()
    {
        int calls = 0;
        GoogleRemoteEmbeddingTokenizer tokenizer = new(
            "gemini-embedding-2",
            (text, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                calls++;
                return ValueTask.FromResult((text.Length + 1) / 2);
            });

        EmbeddingTokenBoundary boundary = await tokenizer
            .GetPrefixBoundaryAsync(new string('a', 100), 10);

        Assert.InRange(boundary.TokenCount, 1, 10);
        Assert.Equal(19, boundary.Index);
        Assert.Equal(2, calls);
        Assert.Equal(
            "google:gemini-embedding-2:remote-count-tokens:v1",
            tokenizer.Identity);
    }

    [Theory]
    [InlineData(EmbeddingPurpose.Document, "title: none | text: evidence")]
    [InlineData(EmbeddingPurpose.Query, "task: search result | query: evidence")]
    public async Task Embedding2PurposeGenerator_FormatsRetrievalInput(
        EmbeddingPurpose purpose,
        string expectedInput)
    {
        RecordingEmbeddingGenerator inner = new();
        using IEmbeddingGenerator<string, Embedding<float>> generator =
            new GoogleGenAIEmbeddingGeneratorProvider.GoogleEmbeddingPurposeGenerator(
                inner,
                "gemini-embedding-2",
                purpose);

        await generator.GenerateAsync(["evidence"]);

        Assert.Equal([expectedInput], inner.Values);
        Assert.Null(inner.Config);
    }

    [Theory]
    [InlineData(EmbeddingPurpose.Document, "RETRIEVAL_DOCUMENT")]
    [InlineData(EmbeddingPurpose.Query, "RETRIEVAL_QUERY")]
    public async Task Embedding001PurposeGenerator_MapsPurposeToTaskType(
        EmbeddingPurpose purpose,
        string expectedTaskType)
    {
        RecordingEmbeddingGenerator inner = new();
        using IEmbeddingGenerator<string, Embedding<float>> generator =
            new GoogleGenAIEmbeddingGeneratorProvider.GoogleEmbeddingPurposeGenerator(
                inner,
                "gemini-embedding-001",
                purpose);

        await generator.GenerateAsync(["evidence"]);

        Assert.Equal(["evidence"], inner.Values);
        Assert.Equal(expectedTaskType, inner.Config?.TaskType);
    }

    [Fact]
    public void Provider_CreatesOfficialGeneratorWithConfiguredDimensions()
    {
        using Client client = new(apiKey: "test-api-key");
        GoogleGenAIEmbeddingGeneratorProvider provider = new(client);
        using IEmbeddingGenerator<string, Embedding<float>> generator =
            provider.Create(
                "gemini-embedding-2",
                768,
                EmbeddingPurpose.Document);

        EmbeddingGeneratorMetadata metadata = Assert.IsType<
            EmbeddingGeneratorMetadata>(
                generator.GetService(typeof(EmbeddingGeneratorMetadata)));
        Assert.Equal("gemini-embedding-2", metadata.DefaultModelId);
        Assert.Equal(768, metadata.DefaultModelDimensions);
    }

    [Theory]
    [InlineData(
        "gemini-embedding-2",
        "google:gemini-embedding-2/768/retrieval-asymmetric-v1")]
    [InlineData(
        "gemini-embedding-001",
        "google:gemini-embedding-001/768/retrieval-task-type-v1")]
    public void Provider_IdentifiesModelDimensionsAndRetrievalStrategy(
        string model,
        string expectedIdentity)
    {
        using Client client = new(apiKey: "test-api-key");
        GoogleGenAIEmbeddingGeneratorProvider provider = new(client);

        Assert.Equal(expectedIdentity, provider.GetEmbeddingIdentity(model, 768));
    }

    [Fact]
    public async Task Provider_DoesNotCreateClientUntilEmbeddingsAreGenerated()
    {
        int calls = 0;
        GoogleGenAIEmbeddingGeneratorProvider provider = new(() =>
        {
            calls++;
            throw new InvalidOperationException("Client creation was deferred.");
        });

        using IEmbeddingGenerator<string, Embedding<float>> generator =
            provider.Create(
                "gemini-embedding-2",
                768,
                EmbeddingPurpose.Query);
        _ = generator.GetService(typeof(EmbeddingGeneratorMetadata));

        Assert.Equal(0, calls);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await generator.GenerateAsync(["query"]));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Provider_RejectsUnknownEmbeddingModel()
    {
        using Client client = new(apiKey: "test-api-key");
        GoogleGenAIEmbeddingGeneratorProvider provider = new(client);

        EmbeddingTokenizerNotSupportedException exception = Assert.Throws<
            EmbeddingTokenizerNotSupportedException>(
                () => provider.CreateTokenizerAsync("unknown-model"));

        Assert.Contains("unknown-model", exception.Message);
        Assert.Contains("gemini-embedding-2", exception.Message);
    }

    private sealed class RecordingEmbeddingGenerator
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public EmbedContentConfig? Config { get; private set; }

        public IReadOnlyList<string> Values { get; private set; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Values = values.ToArray();
            Config = options?.RawRepresentationFactory?.Invoke(this) as EmbedContentConfig;
            return Task.FromResult(
                new GeneratedEmbeddings<Embedding<float>>());
        }

        public object? GetService(
            System.Type serviceType,
            object? serviceKey = null) =>
            serviceType == typeof(EmbeddingGeneratorMetadata)
                ? new EmbeddingGeneratorMetadata(
                    "test",
                    null,
                    "test-model",
                    3)
                : null;

        public void Dispose()
        {
        }
    }
}
