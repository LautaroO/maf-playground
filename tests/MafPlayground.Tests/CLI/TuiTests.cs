using MafPlayground.CLI.Tui;
using Microsoft.Agents.AI.Workflows;

namespace MafPlayground.Tests;

public sealed class TuiTests
{
    [Fact]
    public void RemoteHostOptionReadsAnAbsoluteUrl()
    {
        var command = new MafPlayground.CLI.Commands.TuiCommand().Create();
        var result = command.Parse(["--host", "http://127.0.0.1:5051"]);
        Assert.Empty(result.Errors);
        var option = Assert.IsType<System.CommandLine.Option<string>>(command.Options.Single(o => o.Name == "--host"));
        Assert.Equal("http://127.0.0.1:5051", result.GetValue(option));
        Assert.NotEmpty(command.Parse(["--host", "relative"]).Errors);
    }

    [Fact]
    public void InspectorShowsAccentsAndKeepsControlCharactersEscaped()
    {
        const string text = "Hola, ¿cómo estás?\n\u001b";
        var run = new InspectorRun();
        run.Begin("validate-es", new { translatedText = text });
        string input = Assert.Single(run.Attempts("validate-es")).Input;
        Assert.Contains("Hola, ¿cómo estás?", input, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", input, StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(input);
        Assert.Equal(text, document.RootElement.GetProperty("translatedText").GetString());
    }

    [Fact]
    public void LayoutRetainsForkJoinAndReturnEdges()
    {
        var graph = new WorkflowGraph("start", ["start", "a", "b", "end"],
            [("start", "a"), ("start", "b"), ("a", "end"), ("b", "end"), ("end", "b")]);
        Assert.Equal(4, graph.Nodes.Select(n => (n.X, n.Y)).Distinct().Count());
        Assert.Equal(["a", "b"], graph.Neighbors("start", true));
        Assert.Equal(["a", "b"], graph.Neighbors("end", false));
        Assert.Equal(["b"], graph.Neighbors("end", true));
        Assert.Contains(graph.Lines, line => line.Contains('▲'));
    }

    [Fact]
    public void CorrelatesOverlappingToolInvocationsAndClosesRemainingWorkOnCancellation()
    {
        var run = new InspectorRun();
        run.Start();
        const string tool = "tool/lookup";
        var first = new Invocation("first") { CallId = "call-a" };
        var second = new Invocation("second") { CallId = "call-b" };
        run.BeginTool(tool, first);
        run.BeginTool(tool, second);
        run.Finish(tool, second, "result b", "", System.Diagnostics.Stopwatch.GetTimestamp());

        Assert.True(run.IsActive(tool));
        Assert.False(first.Finished);
        Assert.Equal((tool, 1), run.EventTarget(2));

        run.Cancel();

        Assert.Equal("Cancelled", run.Status);
        Assert.False(run.IsActive(tool));
        Assert.Equal("Cancelled", first.Error);
        Assert.True(first.Finished);
        Assert.NotNull(first.Duration);
        Assert.Equal("result b", second.Output);
        Assert.Empty(second.Error);
        Assert.Equal((tool, 0), run.EventTarget(3));
    }

    [Fact]
    public void ExecutorFailureRetainsInputAndTerminalSafeError()
    {
        InspectorRun run = new();
        run.Apply(new ExecutorInvokedEvent("node", "input"));
        run.Apply(new ExecutorFailedEvent("node", new InvalidOperationException("failed\u001b[2J")));
        var invocation = Assert.Single(run.Attempts("node"));
        Assert.True(invocation.Failed);
        Assert.Contains("input", invocation.Input);
        Assert.DoesNotContain("\u001b", invocation.Error, StringComparison.Ordinal);
    }
}
