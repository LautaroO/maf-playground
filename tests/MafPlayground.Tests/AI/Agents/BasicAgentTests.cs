using System.Collections.Concurrent;
using System.Diagnostics;
using MafPlayground.AI;
using MafPlayground.AI.Agents.BasicAgent;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests;

public sealed class BasicAgentTests
{
    [Fact]
    public async Task Agent_UsesInjectedChatClient()
    {
        using FakeChatClient chatClient = new("Hello from the fake model.");
        BasicAgent basicAgent = new(chatClient);

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
        BasicAgent basicAgent = new(chatClient);

        await basicAgent.Agent.RunAsync("prompt content must not be captured");

        Assert.NotEmpty(stoppedActivities);
        Assert.DoesNotContain(
            stoppedActivities.SelectMany(activity => activity.TagObjects),
            tag => tag.Value is string value &&
                (value.Contains("prompt content", StringComparison.Ordinal) ||
                 value.Contains("response content", StringComparison.Ordinal)));
    }
}
