using MafPlayground.CLI.Tui;

namespace MafPlayground.Tests;

public sealed class LocalCaptureTests
{
    [Fact]
    public void RoundTripPreservesGraphResultAndSelectableFailure()
    {
        var graph = new WorkflowGraph("agent", ["agent", "tool/date"], [("agent", "tool/date")]);
        var run = new InspectorRun();
        run.Start();
        run.Begin("agent", "date?");
        var tool = new Invocation("{}");
        run.BeginTool("tool/date", tool);
        run.Finish("tool/date", tool, "", "timeout", System.Diagnostics.Stopwatch.GetTimestamp());
        run.End("agent", "no date");
        run.Complete("no date");
        var baseline = run.Capture("agent", "ollama:test", "\"date?\"", DateTimeOffset.UtcNow, graph);
        string path = Path.Combine(Path.GetTempPath(), $"capture-{Guid.NewGuid():N}.json");
        try
        {
            baseline.Save(path);
            Assert.Throws<IOException>(() => baseline.Save(path));
            var file = LocalCapture.Read(path);
            var restored = InspectorRun.Restore(file);
            Assert.Equal(run.Result, restored.Result);
            Assert.Equal(run.Elapsed, restored.Elapsed);
            Assert.Equal(run.Events, restored.Events);
            Assert.Equal(run.EventTarget(2), restored.EventTarget(2));
            Assert.Equal("timeout", Assert.Single(restored.Attempts("tool/date")).Error);
            Assert.Equal(run.ToolNodes, restored.ToolNodes);
            Assert.Contains("tool/date", file.Graph().Neighbors("agent", true));
            Assert.Contains("Result unchanged", LocalCapture.Compare(baseline, file));
            Assert.Equal("ollama:test", file.Model);
            string prefix = new('a', 64000);
            string difference = LocalCapture.Compare(file with { Result = prefix + "before" },
                file with { Result = prefix + "after" });
            Assert.Contains("before", difference);
            Assert.Contains("after", difference);
            string arrays = LocalCapture.Compare(file with { Result = "[1,2]" }, file with { Result = "[1,3,4]" });
            Assert.Contains("Result.length: 2 → 3", arrays);
            Assert.Contains("Result[1]", arrays);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(file with { Result = new string('a', 70000) }, InspectorRun.Json));
            Assert.Throws<InvalidDataException>(() => LocalCapture.Read(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsUnrecognizedCaptureVersion()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"version":99}""");
            Assert.Throws<InvalidDataException>(() => LocalCapture.Read(path));
        }
        finally { File.Delete(path); }
    }
}
