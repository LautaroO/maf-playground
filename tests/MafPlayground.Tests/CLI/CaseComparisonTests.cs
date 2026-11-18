using System.Text.Json;
using MafPlayground.CLI.Tui;

namespace MafPlayground.Tests;

public sealed class CaseComparisonTests
{
    [Fact]
    public async Task ComparisonRunsIdenticalSavedInputsAndChecksInPairsEvenWhenAnAssertionFails()
    {
        StudioCase[] cases = [Item("first", 1), Item("second", 2)];
        List<(string Model, StudioCase Case)> observed = [];
        List<CaseOutcome> results = [];
        var jobs = CaseComparison.Schedule(cases, "left", "right", (model, item, _) =>
        {
            observed.Add((model, item));
            var failures = item.Checks.Evaluate("Completed", JsonSerializer.SerializeToElement(model), [], true);
            return Task.FromResult(new CaseOutcome(item.Name, failures));
        });
        await CaseLibrary.ExecuteAsync(jobs, _ => { }, results.Add, CancellationToken.None);

        Assert.Equal([("left", cases[0]), ("right", cases[0]), ("left", cases[1]), ("right", cases[1])], observed);
        Assert.Equal([false, true, false, true], results.Select(outcome => outcome.Failures.Count == 0));
    }

    [Fact]
    public async Task LostRemoteReceiptStopsBothSidesWithoutRetrying()
    {
        var jobs = CaseComparison.Schedule([Item("first", 1), Item("second", 2)], "left", "right", (model, item, _) =>
            model == "left" ? Task.FromResult(new CaseOutcome(item.Name, [])) : throw new IOException("Receipt unavailable"));
        List<CaseOutcome> results = [];
        await CaseLibrary.ExecuteAsync(jobs, _ => { }, results.Add, CancellationToken.None);
        Assert.Equal(2, results.Count);
        Assert.Contains("suite stopped", Assert.Single(results[1].Failures).Message);
    }

    [Theory]
    [InlineData(true, false, 7L)]
    [InlineData(true, true, null)]
    [InlineData(false, false, null)]
    public void TokenSummaryRequiresCompleteCoverageAndDeduplicatesSpans(bool captureComplete, bool hasGap, long? expected)
    {
        var journal = new RemoteJournal("r");
        var snapshot = JsonSerializer.SerializeToElement(new { runId = "r", captureComplete });
        var page = JsonSerializer.SerializeToElement(new
        {
            hasGap,
            events = Enumerable.Range(1, 3).Select(sequence => new
            {
                runId = "r",
                sequence,
                kind = "span",
                data = new { source = "MafPlayground.Studio.Model", traceId = "trace", spanId = sequence == 3 ? "b" : "a", inputTokens = sequence == 3 ? 4 : 3 }
            })
        });
        journal.Apply(snapshot, page);
        Assert.Equal((expected, (long?)null), journal.ModelTokens());
    }

    [Fact]
    public void LocalAgentUsageSurvivesCaptureRoundTripAndMissingUsageRemainsUnknown()
    {
        var graph = new WorkflowGraph("agent", ["agent"], []);
        var run = new InspectorRun();
        run.Start();
        run.Complete("answer", 0, 13);
        var capture = run.Capture("agent", "provider:model", "{}", DateTimeOffset.UnixEpoch, graph);
        var archived = JsonSerializer.Deserialize<LocalCapture>(JsonSerializer.Serialize(capture))!;
        var restored = InspectorRun.Restore(archived).Capture("agent", "provider:model", "{}", capture.Started, graph);
        Assert.Equal((0L, 13L), (restored.InputTokens, restored.OutputTokens));
        run.Complete("no usage reported");
        var unknown = run.Capture("agent", null, "{}", capture.Started, graph);
        Assert.Null(unknown.InputTokens);
        Assert.Null(unknown.OutputTokens);
    }

    private static StudioCase Item(string name, int input) => new(Guid.NewGuid(), name,
        JsonSerializer.SerializeToElement(input), new CaseChecks(Contains: "right"));
}
