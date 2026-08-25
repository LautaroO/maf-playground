using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MafPlayground.AI;
using MafPlayground.AI.Configuration;
using MafPlayground.Providers.Google;
using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.Tokenizers;
using Xunit;

namespace MafPlayground.IntegrationTests;

public sealed class GoogleGenAIProviderContractTests
{
    [GoogleGenAIContractFact]
    public async Task ChatClient_SupportsStructuredOutputAndUsageMetadata()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddGoogleGenAIProvider(configuration);
        services.AddAICore(GetSelection());
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        IChatClient chatClient = serviceProvider.GetRequiredService<IChatClient>();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));

        ChatResponse<ContractResponse> response = await chatClient
            .GetResponseAsync<ContractResponse>(
                "Return the value 'ready'.",
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                },
                new ChatOptions
                {
                    Instructions =
                        "Return only the requested structured response.",
                },
                useJsonSchemaResponseFormat: true,
                timeout.Token);

        Assert.Equal("ready", response.Result.Value, ignoreCase: true);
        Assert.True(response.Usage?.InputTokenCount > 0);
        Assert.True(response.Usage?.OutputTokenCount > 0);
    }

    [GoogleGenAIContractFact]
    public async Task Embeddings_UseSemanticChunkingAndReturnConfiguredDimensions()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddGoogleGenAIProvider(configuration);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        IEmbeddingGeneratorProvider provider = serviceProvider
            .GetServices<IEmbeddingGeneratorProvider>()
            .Single(candidate => candidate.Name == "google");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));

        IngestionDocument ingestionDocument = new("google-embedding-contract");
        IngestionDocumentSection section = new();
        section.Elements.Add(new IngestionDocumentParagraph(
            ".NET is a cross-platform developer platform."));
        section.Elements.Add(new IngestionDocumentParagraph(
            "C# applications run on the .NET runtime."));
        section.Elements.Add(new IngestionDocumentParagraph(
            "Zeus is a deity from ancient Greek mythology."));
        ingestionDocument.Sections.Add(section);
        KnowledgeIngestionSettings settings = new(1)
        {
            ChunkingStrategy = DocumentChunkingStrategy.SemanticSimilarity,
            MaxTokensPerChunk = 64,
            SemanticSimilarityThresholdPercentile = 50,
        };
        using IEmbeddingGenerator<string, Embedding<float>> documentGenerator =
            provider.Create(
                "gemini-embedding-2",
                768,
                EmbeddingPurpose.Document);
        MicrosoftDataIngestionDocumentChunker chunker = new();
        IReadOnlyList<DocumentChunk> chunks = await chunker.ChunkAsync(
            new ExtractedDocument(
                "Google embedding contract",
                ingestionDocument,
                []),
            settings,
            documentGenerator,
            timeout.Token);
        using IEmbeddingGenerator<string, Embedding<float>> queryGenerator =
            provider.Create(
                "gemini-embedding-2",
                768,
                EmbeddingPurpose.Query);

        GeneratedEmbeddings<Embedding<float>> documentEmbeddings =
            await documentGenerator.GenerateAsync(
                chunks.Select(chunk => chunk.Text),
                cancellationToken: timeout.Token);
        ReadOnlyMemory<float> queryEmbedding = await queryGenerator
            .GenerateVectorAsync(
                "grounded retrieval evidence",
                cancellationToken: timeout.Token);

        Assert.Contains(
            "semanticsimilarity:tokenizer:cl100k_base:1.0.1",
            chunker.GetIdentity(settings));
        Assert.NotEmpty(chunks);
        Tokenizer tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
        foreach (DocumentChunk chunk in chunks)
        {
            Assert.InRange(
                tokenizer.CountTokens(chunk.Text, considerNormalization: false),
                1,
                settings.MaxTokensPerChunk);
        }
        Assert.Equal(chunks.Count, documentEmbeddings.Count);
        Assert.All(documentEmbeddings, embedding =>
            Assert.Equal(768, embedding.Vector.Length));
        Assert.Equal(768, queryEmbedding.Length);
    }

    private static AIModelSelection GetSelection()
    {
        AIModelSelection selection = AIModelSelection.Parse(
            Environment.GetEnvironmentVariable("GOOGLE_AI_MODEL") ??
            "google:gemini-3.6-flash");
        return string.Equals(
            selection.Provider,
            "google",
            StringComparison.OrdinalIgnoreCase)
            ? selection
            : throw new InvalidOperationException(
                "Google Gen AI tests require GOOGLE_AI_MODEL=google:<model>.");
    }

    private sealed record ContractResponse(string Value);
}
