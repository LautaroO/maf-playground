using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MafPlayground.AI;
using MafPlayground.AI.Configuration;
using MafPlayground.Providers.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
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
