using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MafPlayground.AI;
using MafPlayground.AI.Configuration;
using MafPlayground.Providers.Ollama;
using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MafPlayground.IntegrationTests;

[Collection(OllamaCollection.Name)]
public sealed class OllamaProviderContractTests
{
    [OllamaContractFact]
    public async Task ChatClient_SupportsStructuredOutputAndUsageMetadata()
    {
        using ServiceProvider services = CreateServices();
        IChatClient chatClient = services.GetRequiredService<IChatClient>();
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
                    Instructions = "Return only the requested structured response.",
                },
                useJsonSchemaResponseFormat: true,
                timeout.Token);

        Assert.Equal("ready", response.Result.Value, ignoreCase: true);
        Assert.True(response.Usage?.InputTokenCount > 0);
        Assert.True(response.Usage?.OutputTokenCount > 0);
    }

    [OllamaContractFact]
    public async Task ChatClient_StreamsText()
    {
        using ServiceProvider services = CreateServices();
        IChatClient chatClient = services.GetRequiredService<IChatClient>();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        List<ChatResponseUpdate> updates = [];

        await foreach (ChatResponseUpdate update in chatClient
            .GetStreamingResponseAsync("Reply with the single word ready.", cancellationToken: timeout.Token))
        {
            updates.Add(update);
        }

        Assert.Contains("ready", string.Concat(updates.Select(update => update.Text)),
            StringComparison.OrdinalIgnoreCase);
    }

    [OllamaContractFact]
    public async Task NomicTokenizer_ChunksAndEmbedsWithInstalledModelMetadata()
    {
        ServiceCollection serviceCollection = new();
        serviceCollection.AddOllamaProvider(CreateConfiguration());
        using ServiceProvider services = serviceCollection.BuildServiceProvider();
        IEmbeddingGeneratorProvider provider = services
            .GetServices<IEmbeddingGeneratorProvider>()
            .Single(candidate => candidate.Name == "ollama");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));

        EmbeddingTokenizer tokenizer = await provider.CreateTokenizerAsync(
            "nomic-embed-text",
            timeout.Token);
        IngestionDocument ingestionDocument = new("ollama-contract");
        IngestionDocumentSection section = new();
        section.Elements.Add(new IngestionDocumentParagraph(string.Join(
            ' ',
            Enumerable.Repeat("retrieval evidence must remain grounded", 20))));
        ingestionDocument.Sections.Add(section);
        ExtractedDocument document = new("Ollama contract", ingestionDocument, []);
        KnowledgeIngestionSettings settings = new(1)
        {
            MaxTokensPerChunk = 24,
            OverlapTokens = 4,
        };

        IReadOnlyList<DocumentChunk> chunks = await
            new MicrosoftDataIngestionDocumentChunker().ChunkAsync(
                document,
                settings,
                tokenizer,
                timeout.Token);
        using IEmbeddingGenerator<string, Embedding<float>> generator =
            provider.Create(
                "nomic-embed-text",
                768,
                EmbeddingPurpose.Document);
        GeneratedEmbeddings<Embedding<float>> embeddings =
            await generator.GenerateAsync(
                chunks.Select(chunk => chunk.Text),
                cancellationToken: timeout.Token);

        Assert.StartsWith(
            "ollama:nomic-embed-text:bert:vocab-sha256:",
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
        Assert.Equal(chunks.Count, embeddings.Count);
        Assert.All(embeddings, embedding => Assert.False(embedding.Vector.IsEmpty));
    }

    private static ServiceProvider CreateServices()
    {
        AIModelSelection selection = GetOllamaSelection();
        IConfiguration configuration = CreateConfiguration();
        ServiceCollection services = new();
        services.AddOllamaProvider(configuration);
        services.AddAICore(selection);
        return services.BuildServiceProvider();
    }

    internal static AIModelSelection GetOllamaSelection()
    {
        AIModelSelection selection = AIModelSelection.Parse(
            Environment.GetEnvironmentVariable("AI_MODEL") ?? "ollama:llama3.1:8b");
        return string.Equals(selection.Provider, "ollama", StringComparison.OrdinalIgnoreCase)
            ? selection
            : throw new InvalidOperationException("Ollama tests require AI_MODEL=ollama:<model>.");
    }

    internal static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{OllamaProviderOptions.ConfigurationSectionName}:Endpoint"] =
                    Environment.GetEnvironmentVariable("AI__PROVIDERS__OLLAMA__ENDPOINT") ??
                    "http://localhost:11434",
            })
            .Build();

    private sealed record ContractResponse(string Value);
}
