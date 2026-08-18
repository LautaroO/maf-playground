using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Tools;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests;

public sealed class CostAggregationTests
{
    [Fact]
    public async Task CostTracking_SumsTranslationRetryModelCalls()
    {
        FakeChatClient innerClient = new(
            """{"translatedText":"Ola"}""",
            CreatePerCallUsage());
        innerClient.EnqueueResponse(
            """{"isValid":false,"confidence":0.4,"issues":[{"severity":"Blocking","code":"SemanticMeaningChanged","description":"Incorrect translation."}]}""");
        innerClient.EnqueueResponse("""{"translatedText":"Hola"}""");
        innerClient.EnqueueResponse(
            """{"isValid":true,"confidence":1,"issues":[],"previousIssueResolutions":[{"issueId":"attempt-1-issue-0-SemanticMeaningChanged","status":"Resolved"}]}""");

        const string model = "retry-model";
        using ServiceProvider provider = CreateProvider(innerClient, model);
        using CostMeasurementCollector collector = new(model);

        TranslationWorkflowResult result = await provider
            .GetRequiredService<TranslationWorkflowRunner>()
            .RunAsync(new TranslationWorkflowRequest("Hello", ["es"]));

        Assert.True(Assert.Single(result.Translations).IsValid);
        Assert.Equal(4, collector.Measurements.Count);
        Assert.Equal(0.04, collector.Measurements.Sum(), precision: 10);
    }

    [Fact]
    public async Task CostTracking_SumsParallelWorkflowBranchModelCalls()
    {
        using BranchAwareChatClient innerClient = new();
        const string model = "branch-model";
        using ServiceProvider provider = CreateProvider(innerClient, model);
        using CostMeasurementCollector collector = new(model);

        TranslationWorkflowResult result = await provider
            .GetRequiredService<TranslationWorkflowRunner>()
            .RunAsync(new TranslationWorkflowRequest("Hello", ["es", "fr"]));

        Assert.All(result.Translations, translation => Assert.True(translation.IsValid));
        Assert.Equal(4, collector.Measurements.Count);
        Assert.Equal(0.04, collector.Measurements.Sum(), precision: 10);
    }

    [Fact]
    public async Task CostTracking_SumsModelTurnsBeforeAndAfterToolInvocation()
    {
        using ToolCallingChatClient innerClient = new();
        const string model = "tool-model";
        using ServiceProvider provider = CreateProvider(innerClient, model);
        using CostMeasurementCollector collector = new(model);

        string response = (await provider.GetRequiredService<BasicAgent>().Agent.RunAsync(
            "What date is it in UTC?")).Text;

        Assert.Equal("The tool call completed.", response);
        Assert.Equal(2, innerClient.CallCount);
        Assert.Equal(2, collector.Measurements.Count);
        Assert.Equal(0.02, collector.Measurements.Sum(), precision: 10);
    }

    private static ServiceProvider CreateProvider(IChatClient chatClient, string model)
    {
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new FakeProvider(chatClient));
        services.AddSingleton<IModelPricingSource>(new FakePricingSource());
        services.AddSingleton<IUserContextAccessor>(new FakeUserContextAccessor(
            new UserContext(
            [
                new KeyValuePair<string, string>(UserContextKeys.TimeZone, "UTC"),
            ])));
        services.AddAIServices(AIModelSelection.Parse($"fake:{model}"));
        services.AddMafPlaygroundObservability(CreateCostConfiguration());
        return services.BuildServiceProvider();
    }

    private static IConfiguration CreateCostConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "true",
                ["Observability:Cost:Enabled"] = "true",
            })
            .Build();

    private static UsageDetails CreatePerCallUsage() => new()
    {
        InputTokenCount = 1_000_000,
        OutputTokenCount = 0,
    };

    private sealed class CostMeasurementCollector : IDisposable
    {
        private readonly MeterListener _listener = new();

        public CostMeasurementCollector(string model)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ObservabilityTelemetry.CostMeterName &&
                    instrument.Name == ObservabilityTelemetry.CostMetricName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            {
                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    if (tag.Key == "gen_ai.request.model" && Equals(tag.Value, model))
                    {
                        Measurements.Enqueue(value);
                        break;
                    }
                }
            });
            _listener.Start();
        }

        public ConcurrentQueue<double> Measurements { get; } = new();

        public void Dispose() => _listener.Dispose();
    }

    private sealed class FakeProvider(IChatClient client) : IChatClientProvider
    {
        public string Name => "fake";

        public IChatClient CreateChatClient(string model) => client;
    }

    private sealed class FakePricingSource : IModelPricingSource
    {
        public string Provider => "fake";

        public bool TryGetPrice(
            string model,
            [NotNullWhen(true)] out ModelTokenPrice? price)
        {
            if (!model.EndsWith("-model", StringComparison.OrdinalIgnoreCase))
            {
                price = null;
                return false;
            }

            price = new ModelTokenPrice("USD", "test-pricing", 0.01m, 0.02m);
            return true;
        }
    }

    private sealed class BranchAwareChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("fake");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool isValidation = options?.Instructions?.StartsWith(
                "Validate whether",
                StringComparison.Ordinal) == true;
            string response = isValidation
                ? """{"isValid":true,"confidence":1,"issues":[]}"""
                : """{"translatedText":"translated"}""";
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, response))
            {
                Usage = CreatePerCallUsage(),
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

    private sealed class ToolCallingChatClient : IChatClient
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
            int call = Interlocked.Increment(ref _callCount);
            ChatMessage response = call == 1
                ? new ChatMessage(
                    ChatRole.Assistant,
                    [
                        new FunctionCallContent(
                            "call-1",
                            CurrentDateTimeTool.FunctionName,
                            new Dictionary<string, object?>
                            {
                                ["timeZoneId"] = "UTC",
                            }),
                    ])
                : new ChatMessage(ChatRole.Assistant, "The tool call completed.");
            return Task.FromResult(new ChatResponse(response)
            {
                Usage = CreatePerCallUsage(),
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
