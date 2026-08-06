using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI.Tools;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests;

public sealed class BasicAgentTests
{
    [Fact]
    public async Task Agent_UsesInjectedChatClient()
    {
        using FakeChatClient chatClient = new("Hello from the fake model.");
        BasicAgent basicAgent = CreateBasicAgent(chatClient);

        string response = (await basicAgent.Agent.RunAsync("Hello")).Text;

        Assert.Equal("Hello from the fake model.", response);
    }

    [Fact]
    public async Task Agent_EmitsMafTelemetryWithoutMessageContentByDefault()
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

        using FakeChatClient chatClient = new("response content must not be captured");
        BasicAgent basicAgent = CreateBasicAgent(chatClient);

        await basicAgent.Agent.RunAsync("prompt content must not be captured");

        Assert.NotEmpty(stoppedActivities);
        Assert.DoesNotContain(
            stoppedActivities.SelectMany(activity => activity.TagObjects),
            tag => tag.Value is string value &&
                (value.Contains("prompt content", StringComparison.Ordinal) ||
                 value.Contains("response content", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Agent_ProvidesCurrentDateTimeToolToModel()
    {
        using FakeChatClient chatClient = new("response");
        BasicAgent basicAgent = CreateBasicAgent(chatClient);

        await basicAgent.Agent.RunAsync("What time is it?");

        ChatOptions options = Assert.Single(chatClient.RequestOptions)!;
        AIFunction function = Assert.IsAssignableFrom<AIFunction>(
            Assert.Single(options.Tools!));
        Assert.Equal(CurrentDateTimeTool.FunctionName, function.Name);
    }

    [Fact]
    public async Task Agent_InjectsTrustedUserContextForEachRun()
    {
        using FakeChatClient chatClient = new("response");
        BasicAgent basicAgent = CreateBasicAgent(chatClient, "America/Argentina/Buenos_Aires");

        await basicAgent.Agent.RunAsync("What is my local time?");

        ChatOptions options = Assert.Single(chatClient.RequestOptions)!;
        Assert.Contains(
            "\"time_zone\":\"America/Argentina/Buenos_Aires\"",
            options.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Preserve exact factual values returned by tools.",
            options.Instructions,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_ModelFailure_EmitsErrorTraceAndStandardDurationMetric()
    {
        using Activity parent = new Activity("model-failure-test").Start();
        ActivityTraceId traceId = parent.TraceId;
        ConcurrentQueue<Activity> stoppedActivities = new();
        using ActivityListener activityListener = new()
        {
            ShouldListenTo = source => source.Name == AITelemetry.AgentSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    stoppedActivities.Enqueue(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);

        ConcurrentQueue<(double Value, KeyValuePair<string, object?>[] Tags)> durations = new();
        using MeterListener meterListener = new();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == AITelemetry.AgentSourceName &&
                instrument.Name == "gen_ai.client.operation.duration")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            if (Activity.Current?.TraceId == traceId)
            {
                durations.Enqueue((value, tags.ToArray()));
            }
        });
        meterListener.Start();

        using FailingChatClient chatClient = new(
            new InvalidOperationException("Sensitive provider details."));
        BasicAgent basicAgent = CreateBasicAgent(chatClient);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await basicAgent.Agent.RunAsync("private prompt"));

        Activity[] errorActivities = stoppedActivities
            .Where(activity => activity.Status == ActivityStatusCode.Error &&
                Equals(
                    activity.GetTagItem(AITelemetry.ErrorTypeTag),
                    typeof(InvalidOperationException).FullName))
            .ToArray();
        Assert.Contains(errorActivities, activity => activity.DisplayName == "chat");
        Assert.Contains(errorActivities, activity =>
            activity.DisplayName.StartsWith("invoke_agent basic-agent", StringComparison.Ordinal));
        Assert.All(errorActivities, errorActivity =>
            Assert.DoesNotContain(
                errorActivity.TagObjects,
                tag => Equals(tag.Value, "Sensitive provider details.")));

        Assert.NotEmpty(durations);
        Assert.All(durations, measurement =>
        {
            Assert.True(measurement.Value >= 0);
            Assert.Contains(measurement.Tags, tag =>
                tag.Key == AITelemetry.ErrorTypeTag &&
                Equals(tag.Value, typeof(InvalidOperationException).FullName));
            Assert.DoesNotContain(measurement.Tags, tag =>
                Equals(tag.Value, "Sensitive provider details."));
        });
    }

    [Fact]
    public async Task Agent_ToolFailure_EmitsErrorToolTraceWithoutSensitiveMessage()
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

        using InvalidTimeZoneToolCallingChatClient chatClient = new();
        BasicAgent basicAgent = CreateBasicAgent(chatClient);

        string response = (await basicAgent.Agent.RunAsync("What time is it?")).Text;

        Assert.Equal("The tool failed safely.", response);
        Activity toolActivity = Assert.Single(
            stoppedActivities,
            activity => activity.Status == ActivityStatusCode.Error &&
                activity.DisplayName.Contains(
                    CurrentDateTimeTool.FunctionName,
                    StringComparison.Ordinal));
        Assert.Equal(
            typeof(ArgumentException).FullName,
            toolActivity.GetTagItem(AITelemetry.ErrorTypeTag));
        Assert.DoesNotContain(
            toolActivity.TagObjects,
            tag => tag.Value is string value &&
                value.Contains("not-a-time-zone", StringComparison.Ordinal));
    }

    private static BasicAgent CreateBasicAgent(
        IChatClient chatClient,
        string timeZoneId = "UTC")
    {
        CurrentDateTimeTool currentDateTimeTool = new(TimeProvider.System);
        FakeUserContextAccessor accessor = new(new UserContext(
        [
            new KeyValuePair<string, string>(UserContextKeys.TimeZone, timeZoneId),
        ]));
        UserContextProvider userContextProvider = new(accessor);
        return new BasicAgent(
            chatClient,
            currentDateTimeTool,
            userContextProvider);
    }

    private sealed class InvalidTimeZoneToolCallingChatClient : IChatClient
    {
        private int _callCount;

        public ChatClientMetadata Metadata { get; } = new("fake");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChatMessage message = Interlocked.Increment(ref _callCount) == 1
                ? new ChatMessage(
                    ChatRole.Assistant,
                    [
                        new FunctionCallContent(
                            "invalid-time-zone-call",
                            CurrentDateTimeTool.FunctionName,
                            new Dictionary<string, object?>
                            {
                                ["timeZoneId"] = "not-a-time-zone",
                            }),
                    ])
                : new ChatMessage(ChatRole.Assistant, "The tool failed safely.");
            return Task.FromResult(new ChatResponse(message));
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
