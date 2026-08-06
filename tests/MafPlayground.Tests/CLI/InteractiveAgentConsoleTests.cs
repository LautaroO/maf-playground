using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI;
using MafPlayground.AI.Tools;
using MafPlayground.Observability;
using MafPlayground.CLI;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests;

public sealed class InteractiveAgentConsoleTests
{
    private static readonly AIModelSelection ModelSelection =
        AIModelSelection.Parse("test:fake-model");

    [Fact]
    public async Task RunAsync_WithPrompt_StreamsResponseAndExits()
    {
        using FakeChatClient chatClient = new("streamed response");
        BasicAgent basicAgent = CreateBasicAgent(chatClient);
        StringWriter output = new();
        StringWriter error = new();
        InteractiveAgentConsole console = new(new StringReader(string.Empty), output, error);

        int exitCode = await console.RunAsync(basicAgent.Agent, ModelSelection, "hello");

        Assert.Equal(0, exitCode);
        Assert.Contains("Agent: streamed response", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunAsync_WithWatch_EmitsLifecycleWithoutPromptContent()
    {
        using FakeChatClient chatClient = new("streamed response");
        BasicAgent basicAgent = CreateBasicAgent(chatClient);
        StringWriter error = new();
        InteractiveAgentConsole console = new(
            new StringReader(string.Empty),
            new StringWriter(),
            error);

        int exitCode = await console.RunAsync(
            basicAgent.Agent,
            ModelSelection,
            "private prompt",
            watch: true);

        string watchOutput = error.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("[watch ", watchOutput, StringComparison.Ordinal);
        Assert.Contains("agent basic-agent started", watchOutput, StringComparison.Ordinal);
        Assert.Contains("agent completed", watchOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("private prompt", watchOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_InteractiveMode_ReusesConversationSession()
    {
        using FakeChatClient chatClient = new("answer");
        BasicAgent basicAgent = CreateBasicAgent(chatClient);
        StringWriter output = new();
        InteractiveAgentConsole console = new(
            new StringReader("first\nsecond\n/exit\n"),
            output,
            new StringWriter());

        int exitCode = await console.RunAsync(
            basicAgent.Agent,
            ModelSelection,
            prompt: null);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, chatClient.Requests.Count);
        Assert.Equal("first", chatClient.Requests[0].Last().Text);
        Assert.Equal("second", chatClient.Requests[1].Last().Text);
        Assert.True(chatClient.Requests[1].Count > chatClient.Requests[0].Count);
    }

    [Fact]
    public async Task RunAsync_EmitsProviderNeutralTestHarnessTrace()
    {
        ConcurrentQueue<Activity> stoppedActivities = new();
        using ActivityListener listener = new()
        {
            ShouldListenTo = source =>
                source.Name == ObservabilityTelemetry.TestHarnessSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = stoppedActivities.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        using FakeChatClient chatClient = new("answer");
        BasicAgent basicAgent = CreateBasicAgent(chatClient);
        InteractiveAgentConsole console = new(
            new StringReader(string.Empty),
            new StringWriter(),
            new StringWriter());

        int exitCode = await console.RunAsync(
            basicAgent.Agent,
            ModelSelection,
            "prompt content must not be captured");

        Activity activity = Assert.Single(stoppedActivities);
        Assert.Equal(0, exitCode);
        Assert.Equal("agent.test.turn", activity.OperationName);
        Assert.Equal("test", activity.GetTagItem("gen_ai.provider.name"));
        Assert.Equal("fake-model", activity.GetTagItem("gen_ai.request.model"));
        Assert.Equal("single", activity.GetTagItem("maf_playground.harness.mode"));
        Assert.Equal("success", activity.GetTagItem("maf_playground.outcome"));
        Assert.DoesNotContain(
            activity.TagObjects,
            tag => Equals(tag.Value, "prompt content must not be captured"));
    }

    [Fact]
    public async Task RunAsync_ModelFailure_EmitsErrorTraceAndOperationMetrics()
    {
        AIModelSelection modelSelection = AIModelSelection.Parse("test:error-model");
        using HarnessTelemetryCapture telemetry = new(modelSelection.Model);
        using FailingChatClient chatClient = new(
            new InvalidOperationException("Sensitive provider details."));
        BasicAgent basicAgent = CreateBasicAgent(chatClient);
        StringWriter error = new();
        InteractiveAgentConsole console = new(
            new StringReader(string.Empty),
            new StringWriter(),
            error);

        int exitCode = await console.RunAsync(
            basicAgent.Agent,
            modelSelection,
            "private prompt");

        Assert.Equal(1, exitCode);
        Activity activity = Assert.Single(telemetry.Activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("error", activity.GetTagItem(AITelemetry.OutcomeTag));
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            activity.GetTagItem(AITelemetry.ErrorTypeTag));
        Assert.DoesNotContain(
            activity.TagObjects,
            tag => Equals(tag.Value, "Sensitive provider details."));
        Assert.DoesNotContain(
            "Sensitive provider details.",
            error.ToString(),
            StringComparison.Ordinal);
        AssertOperationMetrics(
            telemetry.Measurements,
            "error",
            typeof(InvalidOperationException).FullName!);
    }

    [Fact]
    public async Task RunAsync_Timeout_EmitsErrorTraceAndOperationMetrics()
    {
        AIModelSelection modelSelection = AIModelSelection.Parse("test:timeout-model");
        using HarnessTelemetryCapture telemetry = new(modelSelection.Model);
        using FailingChatClient chatClient = new(waitForCancellation: true);
        BasicAgent basicAgent = CreateBasicAgent(chatClient);
        InteractiveAgentConsole console = new(
            new StringReader(string.Empty),
            new StringWriter(),
            new StringWriter(),
            TimeSpan.FromMilliseconds(20));

        int exitCode = await console.RunAsync(
            basicAgent.Agent,
            modelSelection,
            "private prompt");

        Assert.Equal(1, exitCode);
        Activity activity = Assert.Single(telemetry.Activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("timeout", activity.GetTagItem(AITelemetry.OutcomeTag));
        Assert.Equal(
            typeof(TimeoutException).FullName,
            activity.GetTagItem(AITelemetry.ErrorTypeTag));
        AssertOperationMetrics(
            telemetry.Measurements,
            "timeout",
            typeof(TimeoutException).FullName!);
    }

    private static void AssertOperationMetrics(
        ConcurrentQueue<MetricMeasurement> measurements,
        string outcome,
        string errorType)
    {
        Assert.Equal(3, measurements.Count);
        Assert.Single(measurements, measurement =>
            measurement.Name == AITelemetry.OperationCountMetricName &&
            measurement.Value == 1);
        Assert.Single(measurements, measurement =>
            measurement.Name == AITelemetry.OperationFailureMetricName &&
            measurement.Value == 1);
        Assert.Single(measurements, measurement =>
            measurement.Name == AITelemetry.OperationDurationMetricName &&
            measurement.Value >= 0);
        Assert.All(measurements, measurement =>
        {
            Assert.Contains(measurement.Tags, tag =>
                tag.Key == AITelemetry.OutcomeTag && Equals(tag.Value, outcome));
            Assert.Contains(measurement.Tags, tag =>
                tag.Key == AITelemetry.ErrorTypeTag && Equals(tag.Value, errorType));
        });
    }

    private static BasicAgent CreateBasicAgent(IChatClient chatClient)
    {
        CurrentDateTimeTool currentDateTimeTool = new(TimeProvider.System);
        FakeUserContextAccessor accessor = new(new UserContext(
        [
            new KeyValuePair<string, string>(UserContextKeys.TimeZone, "UTC"),
        ]));
        return new BasicAgent(
            chatClient,
            currentDateTimeTool,
            new UserContextProvider(accessor),
            MafPlayground.AI.Guards.AgentGuardPipeline.CreateDisabled(),
            Microsoft.Extensions.Options.Options.Create(new BasicAgentOptions()));
    }

    private sealed record MetricMeasurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags);

    private sealed class HarnessTelemetryCapture : IDisposable
    {
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener = new();

        public HarnessTelemetryCapture(string model)
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source =>
                    source.Name == ObservabilityTelemetry.TestHarnessSourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (Equals(activity.GetTagItem("gen_ai.request.model"), model))
                    {
                        Activities.Enqueue(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AITelemetry.OperationMeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Capture(instrument, value, tags, model));
            _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                Capture(instrument, value, tags, model));
            _meterListener.Start();
        }

        public ConcurrentQueue<Activity> Activities { get; } = new();

        public ConcurrentQueue<MetricMeasurement> Measurements { get; } = new();

        public void Dispose()
        {
            _meterListener.Dispose();
            _activityListener.Dispose();
        }

        private void Capture<T>(
            Instrument instrument,
            T value,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            string model)
            where T : struct, IConvertible
        {
            KeyValuePair<string, object?>[] copiedTags = tags.ToArray();
            if (copiedTags.Any(tag =>
                    tag.Key == "gen_ai.request.model" && Equals(tag.Value, model)))
            {
                Measurements.Enqueue(new MetricMeasurement(
                    instrument.Name,
                    value.ToDouble(null),
                    copiedTags));
            }
        }
    }
}
