using System.Text.Json;
using MafPlayground.CLI.Tui;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace MafPlayground.Tests;

public sealed class GraphInspectionTests
{
    [Fact]
    public void ComparisonOpensSelectedNodeAndShowsEachRunsOutput()
    {
        var graph = new WorkflowGraph("node", ["node"], []);
        GraphEvidence Evidence(string output)
        {
            var run = new InspectorRun();
            run.Begin("node", "request");
            run.End("node", output);
            return GraphEvidence.From(run.Capture("example", null, "", DateTimeOffset.UnixEpoch, graph));
        }

        using var stop = new CancellationTokenSource();
        using var terminal = new StudioTerminal(stop, "ansi");
        string[]? outputs = null;
        StudioTerminal.App.AddTimeout(TimeSpan.FromMilliseconds(10), () =>
        {
            var nodes = Assert.Single(StudioTerminal.App.TopRunnableView!.SubViews.OfType<ListView>());
            nodes.MoveDown();
            StudioTerminal.App.AddTimeout(TimeSpan.FromMilliseconds(10), () =>
            {
                var fields = Assert.Single(StudioTerminal.App.TopRunnableView!.SubViews.OfType<ListView>());
                fields.MoveDown();
                outputs = StudioTerminal.App.TopRunnableView!.SubViews.OfType<StudioTextView>().Select(view => view.Text).ToArray();
                StudioTerminal.App.RequestStop();
                return false;
            });
            nodes.NewKeyDownEvent(Key.Enter);
            StudioTerminal.App.RequestStop();
            return false;
        });
        GraphInspectionDialog.Compare(Evidence("before"), Evidence("after"), "summary");
        Assert.Collection(outputs!, baseline => Assert.Contains("before", baseline), candidate => Assert.Contains("after", candidate));
    }

    [Fact]
    public void LocalReplayReconstructsOverlappingCallsWithoutShowingFutureOutput()
    {
        var graph = new WorkflowGraph("agent", ["agent", "tool"], [("agent", "tool"), ("tool", "agent")]);
        var run = new InspectorRun();
        run.Begin("agent", "request");
        var first = new Invocation("first") { CallId = "one" };
        var second = new Invocation("second") { CallId = "two" };
        run.BeginTool("tool", first);
        run.BeginTool("tool", second);
        run.Finish("tool", second, "future output", "", System.Diagnostics.Stopwatch.GetTimestamp());
        run.Finish("tool", first, "", "failure", System.Diagnostics.Stopwatch.GetTimestamp());
        var capture = run.Capture("agent", null, "", DateTimeOffset.UnixEpoch, graph);
        var history = GraphEvidence.From(capture);
        Assert.Contains("· tool", history.ReplayFrame(0));
        Assert.Contains("▶ tool", history.ReplayFrame(2));
        Assert.DoesNotContain("future output", history.ReplayFrame(2));
        Assert.Contains("▶ tool", history.ReplayFrame(3));
        Assert.Contains("future output", history.ReplayFrame(3));
        Assert.Contains("× tool", history.ReplayFrame(4));
        Assert.Contains("▲", history.ReplayFrame(4));

        var updated = capture with { Invocations = new() { ["tool"] = [new Invocation("changed") { Finished = true }] } };
        var candidate = GraphEvidence.From(updated with { Events = [], Links = [] });
        var pairs = GraphInspectionDialog.Pair(history, candidate, "tool");
        Assert.Equal(["one", "two"], pairs.Select(p => p.Baseline!.Id));
        Assert.False(pairs[0].Baseline!.SameEvidence(pairs[0].Candidate!));
        Assert.Equal("failure", pairs[0].Baseline!.Error);
        Assert.Null(pairs[1].Candidate);
    }

    [Fact]
    public void RemoteRunWithoutAnExecutorGraphUsesRetainedRunInputAndResult()
    {
        var journal = new RemoteJournal("r");
        var snapshot = JsonSerializer.SerializeToElement(new { runId = "r", request = new { entityId = "chat", input = "hello" }, status = 2, result = "world" });
        journal.Apply(snapshot, JsonSerializer.SerializeToElement(new
        {
            events = new[] { new { runId = "r", sequence = 1, kind = "running" }, new { runId = "r", sequence = 2, kind = "Completed" } }
        }));
        var evidence = GraphEvidence.From(journal);
        Assert.Contains("▶ Run · chat", evidence.ReplayFrame(0));
        Assert.Contains("✓ Run · chat", evidence.ReplayFrame(1));
        Assert.DoesNotContain("world", evidence.ReplayFrame(0));
        var call = Assert.Single(evidence.Calls[Assert.Single(evidence.Nodes)]);
        Assert.Equal("\"hello\"", call.Input);
        Assert.Equal("\"world\"", call.Output);
    }

    [Fact]
    public void RemoteReplayAndDrilldownRespectGapsPayloadStateAndInvocationIdentity()
    {
        static object Event(int sequence, string kind, string value, int captureState = 0) => new
        {
            runId = "r",
            sequence,
            kind,
            invocationId = "i",
            executorId = "n",
            captureState,
            data = new { Id = "payload-" + sequence, ContentType = "application/json", Value = value }
        };
        var journal = new RemoteJournal("r");
        journal.Apply(JsonSerializer.SerializeToElement(new
        {
            runId = "r",
            latestSequence = 8,
            topology = new { nodes = new[] { new { id = "n" } }, edges = Array.Empty<object>() }
        }), JsonSerializer.SerializeToElement(new
        {
            events = new[] { Event(5, "executor_input", "masked", 1), Event(6, "executor_output", "short", 2),
                Event(7, "executor_completed", ""), Event(8, "executor_input", "uncorrelated") },
            hasGap = true
        }));
        var evidence = GraphEvidence.From(journal);
        Assert.Contains("Partial retained journal", evidence.ReplayFrame(0));
        Assert.Contains("· n", evidence.ReplayFrame(0));
        Assert.DoesNotContain("short", evidence.ReplayFrame(0));
        Assert.Contains("✓ n", evidence.ReplayFrame(2));
        var call = Assert.Single(evidence.Calls["n"]);
        Assert.Equal("i", call.Id);
        Assert.Contains("[redacted]", call.Input);
        Assert.Contains("[truncated]", call.Output);
        Assert.DoesNotContain("payload-", call.Output);
    }
}
