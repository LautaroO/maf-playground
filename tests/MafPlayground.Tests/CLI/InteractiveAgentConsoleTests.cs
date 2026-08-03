using System.Collections.Concurrent;
using System.Diagnostics;
using MafPlayground.AI.Agents.BasicAgent;
using MafPlayground.AI;
using MafPlayground.Observability;
using MafPlayground.CLI;

namespace MafPlayground.Tests;

public sealed class InteractiveAgentConsoleTests
{
    private static readonly AIModelSelection ModelSelection =
        AIModelSelection.Parse("test:fake-model");

    [Fact]
    public async Task RunAsync_WithPrompt_StreamsResponseAndExits()
    {
        using FakeChatClient chatClient = new("streamed response");
        BasicAgent basicAgent = new(chatClient);
        StringWriter output = new();
        StringWriter error = new();
        InteractiveAgentConsole console = new(new StringReader(string.Empty), output, error);

        int exitCode = await console.RunAsync(basicAgent.Agent, ModelSelection, "hello");

        Assert.Equal(0, exitCode);
        Assert.Contains("Agent: streamed response", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunAsync_InteractiveMode_ReusesConversationSession()
    {
        using FakeChatClient chatClient = new("answer");
        BasicAgent basicAgent = new(chatClient);
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
        BasicAgent basicAgent = new(chatClient);
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
}
