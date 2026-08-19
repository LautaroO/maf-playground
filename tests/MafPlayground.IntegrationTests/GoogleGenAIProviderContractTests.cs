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
    public async Task Embeddings_UseRemoteTokenCountsAndReturnConfiguredDimensions()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddGoogleGenAIProvider(configuration);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        IEmbeddingGeneratorProvider provider = serviceProvider
            .GetServices<IEmbeddingGeneratorProvider>()
            .Single(candidate => candidate.Name == "google");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));

        EmbeddingTokenizer tokenizer = await provider.CreateTokenizerAsync(
            "gemini-embedding-2",
            timeout.Token);
        IngestionDocument ingestionDocument = new("google-embedding-contract");
        IngestionDocumentSection section = new();
        section.Elements.Add(new IngestionDocumentParagraph(string.Join(
            ' ',
            Enumerable.Repeat("retrieval evidence must remain grounded", 20))));
        ingestionDocument.Sections.Add(section);
        KnowledgeIngestionSettings settings = new(1)
        {
            MaxTokensPerChunk = 24,
            OverlapTokens = 4,
        };
        IReadOnlyList<DocumentChunk> chunks = await
            new MicrosoftDataIngestionDocumentChunker().ChunkAsync(
                new ExtractedDocument(
                    "Google embedding contract",
                    ingestionDocument,
                    []),
                settings,
                tokenizer,
                timeout.Token);
        using IEmbeddingGenerator<string, Embedding<float>> documentGenerator =
            provider.Create(
                "gemini-embedding-2",
                768,
                EmbeddingPurpose.Document);
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

        Assert.StartsWith(
            "google:gemini-embedding-2:remote-count-tokens:v1",
            tokenizer.Identity,
            StringComparison.Ordinal);
        Assert.True(chunks.Count > 1);
        foreach (DocumentChunk chunk in chunks)
        {
            Assert.InRange(
                await tokenizer.CountTokensAsync(chunk.Text, timeout.Token),
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
