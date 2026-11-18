using System.Text.Json;
using MafPlayground.CLI.Tui;

namespace MafPlayground.Tests;

public sealed class RemoteJournalTests
{
    [Fact]
    public void ReplayedPagesPreserveCursorAndMarkRetentionGaps()
    {
        var journal = new RemoteJournal("run");
        var snapshot = JsonSerializer.SerializeToElement(new { runId = "run", isTerminal = true, latestSequence = 5 });
        var page = JsonSerializer.SerializeToElement(new { hasGap = true, nextCursor = 5, events = new[] { new { runId = "run", sequence = 4 } } });
        journal.Apply(snapshot, page);
        journal.Apply(snapshot, page);
        Assert.True(journal.Finished);
        Assert.True(journal.HasGap);
        Assert.Equal(5, journal.Cursor);
        Assert.Single(journal.Events);

        journal.Apply(JsonSerializer.SerializeToElement(new { runId = "run", isTerminal = true, latestSequence = 6 }),
            JsonSerializer.SerializeToElement(new { hasGap = false, nextCursor = 6, events = new[] { new { runId = "run", sequence = 6 } } }));
        Assert.Equal(2, journal.Events.Count());
    }

    [Fact]
    public void GraphMarksConcurrentExecutorsAndKeepsOverlappingAttemptsActive()
    {
        static JsonElement Event(string executorId, string invocationId, string kind) =>
            JsonSerializer.SerializeToElement(new { executorId, invocationId, kind });
        var states = RemoteJournal.NodeStates([Event("a", "a1", "executor_started"), Event("b", "b1", "executor_started"),
            Event("a", "a2", "executor_started"), Event("a", "a2", "executor_completed"), Event("b", "b1", "executor_input")]);
        var graph = RemoteJournal.Graph(JsonSerializer.SerializeToElement(new
        {
            startNodeId = "a",
            nodes = new[] { new { id = "a" }, new { id = "b" } },
            edges = new[] { new { sourceIds = new[] { "a" }, targetIds = new[] { "b" } } },
        }), states);
        Assert.Contains("▶ a", graph);
        Assert.Contains("▶ b", graph);
        Assert.Contains('►', graph);
    }

    [Fact]
    public void FailuresPreferTheModelCauseAndExcludeCancellationOnlySpans()
    {
        static JsonElement Event(int sequence, string kind, string invocationId, string spanId) =>
            JsonSerializer.SerializeToElement(new { sequence, kind, invocationId, spanId, data = new { Status = "Error", SpanId = spanId } });
        var failures = RemoteJournal.Failures([Event(1, "model_failed", "i1", "s1"), Event(2, "executor_failed", "i1", "s1"),
            Event(3, "span", "i1", "s1"), Event(4, "model_cancelled", "i2", "s2"), Event(5, "span", "i2", "s2"),
            Event(6, "span", "i3", "s3")]);
        Assert.Equal([1L, 6L], failures.Select(e => e.Number("sequence")));
    }

    [Fact]
    public void OnlyRecordedModelSpansContributeToUsageAndMissingValuesStayUnknown()
    {
        var journal = new RemoteJournal("a");
        var data = new { SpanId = "s", Source = "MafPlayground.Studio.Model", InputTokens = 8, OutputTokens = (int?)null, Cost = (double?)null };
        var snapshot = JsonSerializer.SerializeToElement(new { runId = "a" });
        journal.Apply(snapshot, JsonSerializer.SerializeToElement(new
        {
            nextCursor = 2,
            events = new[] { new { runId = "a", sequence = 1, kind = "span", data }, new { runId = "a", sequence = 2, kind = "span", data } }
        }));
        Assert.Contains("Observed model attempts  1", journal.Metrics());
        Assert.Contains("Input tokens  8", journal.Metrics());
        Assert.Contains("Output tokens  unavailable", journal.Metrics());
        Assert.Contains("Cost  unavailable", journal.Metrics());
    }
}
