using System.Runtime.CompilerServices;
using MafPlayground.AI;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Workflows.Translation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests.AI.Guards;

public sealed class WorkflowBudgetGuardTests
{
    [Fact]
    public async Task TranslationFanOutAndValidation_ShareOneModelCallBudget()
    {
        using TranslationChatClient chatClient = new();
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new FakeProvider(chatClient));
        services.AddAIServices(AIModelSelection.Parse("fake:translation"));
        services.Configure<AIGuardOptions>(options =>
            options.Profiles = new Dictionary<string, GuardProfileOptions>
            {
                ["workflow-budget"] = new()
                {
                    Budget = new BudgetGuardOptions
                    {
                        Enabled = true,
                        MaxModelCalls = 3,
                        MaxToolCalls = 8,
                        MaxInputTokens = 50_000,
                        MaxOutputTokens = 10_000,
                        MaxOutputTokensPerCall = 2_048,
                    },
                },
            });
        services.Configure<TranslationWorkflowOptions>(options =>
        {
            options.GuardProfile = "workflow-budget";
            options.SupportedTargetLanguages = ["es", "fr"];
        });
        using ServiceProvider provider = services.BuildServiceProvider();

        TranslationWorkflowResult result = await provider
            .GetRequiredService<TranslationWorkflowRunner>()
            .RunAsync(new TranslationWorkflowRequest("Hello", ["es", "fr"]));

        Assert.Equal(3, chatClient.CallCount);
        Assert.Contains(result.Translations, translation =>
            !translation.IsValid &&
            translation.Error?.Contains("model_calls", StringComparison.Ordinal) == true);
    }

    private sealed class FakeProvider(IChatClient client) : IChatClientProvider
    {
        public string Name => "fake";

        public IChatClient CreateChatClient(string model) => client;
    }

    private sealed class TranslationChatClient : IChatClient
    {
        private int _callCount;

        public int CallCount => _callCount;

        public ChatClientMetadata Metadata { get; } = new("fake");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            bool validation = options?.Instructions?.StartsWith(
                "Validate whether",
                StringComparison.Ordinal) == true;
            string response = validation
                ? """{"isValid":true,"confidence":1,"issues":[]}"""
                : """{"translatedText":"translated"}""";
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, response))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 10,
                    OutputTokenCount = 10,
                },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
