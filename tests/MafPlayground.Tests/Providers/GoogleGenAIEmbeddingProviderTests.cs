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
    [InlineData(EmbeddingPurpose.Document, "RETRIEVAL_DOCUMENT")]
    [InlineData(EmbeddingPurpose.Query, "RETRIEVAL_QUERY")]
    public async Task PurposeGenerator_MapsNeutralPurposeToGoogleTaskType(
        EmbeddingPurpose purpose,
        string expectedTaskType)
    {
        RecordingEmbeddingGenerator inner = new();
        using IEmbeddingGenerator<string, Embedding<float>> generator =
            new GoogleGenAIEmbeddingGeneratorProvider.GoogleEmbeddingPurposeGenerator(
                inner,
                purpose);

        await generator.GenerateAsync(["evidence"]);

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

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Config = Assert.IsType<EmbedContentConfig>(
                options?.RawRepresentationFactory?.Invoke(this));
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
