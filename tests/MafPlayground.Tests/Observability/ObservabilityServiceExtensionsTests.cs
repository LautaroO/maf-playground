using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MafPlayground.Tests;

public sealed class ObservabilityServiceExtensionsTests
{
    [Fact]
    public void AddMafPlaygroundObservability_BindsAgentOptionsWhenExportIsDisabled()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "false",
                ["Observability:AgentFramework:EnableSensitiveData"] = "true",
            })
            .Build();
        ServiceCollection services = new();

        services.AddMafPlaygroundObservability(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        AgentTelemetryOptions options =
            provider.GetRequiredService<IOptions<AgentTelemetryOptions>>().Value;
        Assert.True(options.EnableSensitiveData);
    }

    [Fact]
    public void AddMafPlaygroundObservability_RejectsEmptyServiceNameWhenEnabled()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "true",
                ["Observability:ServiceName"] = " ",
            })
            .Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddMafPlaygroundObservability(configuration));

        Assert.Contains("ServiceName", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CostTracking_EmitsConfiguredPerMillionEstimate(bool streaming)
    {
        IConfiguration configuration = CreateCostConfiguration();
        FakeChatClient innerClient = new("response", new UsageDetails
        {
            InputTokenCount = 1_000_000,
            OutputTokenCount = 500_000,
        });
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new FakeProvider(innerClient));
        services.AddSingleton<IModelPricingSource>(new FakePricingSource());
        services.AddAIServices(AIModelSelection.Parse("fake:model:v1"));
        services.AddMafPlaygroundObservability(configuration);

        double? observedCost = null;
        KeyValuePair<string, object?>[] observedTags = [];
        using MeterListener meterListener = new();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ObservabilityTelemetry.CostMeterName &&
                instrument.Name == ObservabilityTelemetry.CostMetricName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            observedCost = value;
            observedTags = tags.ToArray();
        });
        meterListener.Start();

        using ServiceProvider provider = services.BuildServiceProvider();
        IChatClient chatClient = provider.GetRequiredService<IChatClient>();
        using Activity activity = new Activity("cost-test").Start();

        if (streaming)
        {
            await foreach (ChatResponseUpdate _ in
                chatClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
            {
            }
        }
        else
        {
            await chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        }

        Assert.Equal(0.02, observedCost);
        Assert.Contains(observedTags, tag =>
            tag.Key == "maf_playground.cost.currency" && Equals(tag.Value, "USD"));
        Assert.Equal(
            0.02,
            activity.GetTagItem("maf_playground.gen_ai.cost"));
    }

    [Fact]
    public async Task CostTracking_DoesNotTreatMissingUsageAsZeroCost()
    {
        IConfiguration configuration = CreateCostConfiguration();
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new FakeProvider(new FakeChatClient("response")));
        services.AddSingleton<IModelPricingSource>(new FakePricingSource());
        services.AddAIServices(AIModelSelection.Parse("fake:model:v1"));
        services.AddMafPlaygroundObservability(configuration);

        int measurementCount = 0;
        using MeterListener meterListener = new();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == ObservabilityTelemetry.CostMetricName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((_, _, _, _) => measurementCount++);
        meterListener.Start();

        using ServiceProvider provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IChatClient>()
            .GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal(0, measurementCount);
    }

    [Fact]
    public async Task CostTracking_AttachesEstimateToMafModelCallSpan()
    {
        ConcurrentQueue<Activity> stoppedActivities = new();
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == AITelemetry.AgentSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stoppedActivities.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        FakeChatClient innerClient = new("response", new UsageDetails
        {
            InputTokenCount = 1_000_000,
            OutputTokenCount = 500_000,
        });
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new FakeProvider(innerClient));
        services.AddSingleton<IModelPricingSource>(new FakePricingSource());
        services.AddSingleton<IUserContextAccessor>(new FakeUserContextAccessor(
            new UserContext(
            [
                new KeyValuePair<string, string>(UserContextKeys.TimeZone, "UTC"),
            ])));
        services.AddAIServices(AIModelSelection.Parse("fake:model:v1"));
        services.AddMafPlaygroundObservability(CreateCostConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();
        await provider.GetRequiredService<BasicAgent>().Agent.RunAsync("hello");

        Activity costActivity = Assert.Single(
            stoppedActivities,
            activity => activity.GetTagItem("maf_playground.gen_ai.cost") is not null);
        Assert.Equal(
            1_000_000L,
            Convert.ToInt64(costActivity.GetTagItem("gen_ai.usage.input_tokens")));
        Assert.Equal(
            500_000L,
            Convert.ToInt64(costActivity.GetTagItem("gen_ai.usage.output_tokens")));
    }

    private static IConfiguration CreateCostConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Enabled"] = "true",
                ["Observability:Cost:Enabled"] = "true",
            })
            .Build();

    private sealed class FakeProvider(FakeChatClient client) : IChatClientProvider
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
            if (!string.Equals(model, "model:v1", StringComparison.OrdinalIgnoreCase))
            {
                price = null;
                return false;
            }

            price = new ModelTokenPrice("USD", "test-pricing", 0.01m, 0.02m);
            return true;
        }
    }
}
