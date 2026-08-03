using System.Collections.Concurrent;
using System.Diagnostics;
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
}
