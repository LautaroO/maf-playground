using System.Text.Json;
using MafPlayground.CLI.Tui;

namespace MafPlayground.Tests;

public sealed class InspectionPresentationTests
{
    [Fact]
    public void SummaryRetainsFailedValidationAndMakesMissingRequestedLanguagesVisible()
    {
        var input = ResultPresentation.Parse("""{"targetLanguages":["es","fr"]}""");
        const string raw = """{"Translations":[{"TargetLanguage":"ES","TranslatedText":"hola","IsValid":false,"Attempts":2,"Issues":[{"Severity":"Blocking","Code":"Meaning","Description":"Lost a qualifier"}]}]}""";
        var output = ResultPresentation.Parse(raw);
        string summary = ResultPresentation.Format("translation-workflow", input, output, raw, false);
        Assert.Contains("Requested languages    es, fr", summary);
        Assert.Contains("ES   ·   Rejected   ·   attempts 2", summary);
        Assert.Contains("hola", summary);
        Assert.Contains("Lost a qualifier", summary);
        Assert.Contains("fr   ·   missing", summary);
        Assert.DoesNotContain("es   ·   missing", summary);
        Assert.Equal(raw, ResultPresentation.Format("translation-workflow", input, output, raw, true));
    }

    [Fact]
    public void LocalTimelineShowsLanguageCorrelatedPayloadsAndConcurrentInvocations()
    {
        var graph = new WorkflowGraph("translate", ["translate", "translation-aggregate"], [("translate", "translation-aggregate")]);
        var run = new InspectorRun();
        run.Begin("translate", "original");
        run.Begin("translation-aggregate", new { translation = new { targetLanguage = "es" } });
        run.End("translate", "translated");
        run.End("translation-aggregate", "spanish");
        run.Begin("translation-aggregate", new { translation = new { targetLanguage = "fr" } });
        run.End("translation-aggregate", "french");
        var capture = run.Capture("translation-workflow", null, "", DateTimeOffset.UnixEpoch, graph);
        var rows = InvocationTimeline.Entries(GraphEvidence.From(capture));
        Assert.Equal([true, true, false], rows.Select(row => row.Concurrent));
        Assert.Equal("translation-aggregate #1 · es", rows[1].Label);
        Assert.Contains("spanish", rows[1].Details);
        Assert.DoesNotContain("french", rows[1].Details);
        Assert.Equal((2, 4), (rows[1].Start, rows[1].End));
        Assert.DoesNotContain("━", rows[1].Bar(6, null)[..1]);
    }

    [Fact]
    public void ElapsedTimeBarsDistinguishEightSecondCallFromOneSecondCallAfterCaptureRestore()
    {
        var graph = new WorkflowGraph("node", ["node"], []);
        var run = new InspectorRun();
        run.Start();
        long origin = System.Diagnostics.Stopwatch.GetTimestamp(), hz = System.Diagnostics.Stopwatch.Frequency;
        run.Begin("node", "slow", origin);
        run.End("node", "first", timestamp: origin + 8 * hz);
        run.Begin("node", "fast", origin + 8 * hz);
        run.End("node", "second", timestamp: origin + 9 * hz);
        run.Complete("done");
        var capture = run.Capture("sample", null, "", DateTimeOffset.UnixEpoch, graph);
        var restored = JsonSerializer.Deserialize<LocalCapture>(JsonSerializer.Serialize(capture, InspectorRun.Json), InspectorRun.Json)!;
        var rows = InvocationTimeline.Entries(GraphEvidence.From(restored));
        var scale = InvocationTimeline.TimeScale(rows, restored.Elapsed);
        Assert.NotNull(scale);
        Assert.Equal(TimeSpan.FromSeconds(8), rows[0].Call.Duration);
        Assert.Equal(TimeSpan.FromSeconds(8), rows[1].Call.StartOffset - rows[0].Call.StartOffset);
        Assert.True(rows[0].Bar(4, scale).Count(c => c == '━') >= 9);
        Assert.True(rows[1].Bar(4, scale).Count(c => c == '━') <= 3);
    }

    [Fact]
    public void RemoteTimelineUsesRecordedBoundariesAndMarksMissingEvidence()
    {
        var journal = new RemoteJournal("r");
        object Event(int sequence, string kind, string invocationId, object value) => new
        {
            runId = "r",
            sequence,
            kind,
            executorId = "translation-aggregate",
            invocationId,
            timestamp = DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            data = new { value }
        };
        journal.Apply(JsonSerializer.SerializeToElement(new { runId = "r" }), JsonSerializer.SerializeToElement(new
        {
            hasGap = true,
            events = new[] { Event(4, "executor_completed", "partial", ""), Event(5, "executor_started", "fr", ""),
                Event(6, "executor_input", "fr", new { Translation = new { TargetLanguage = "fr" } }),
                Event(7, "executor_output", "fr", "bonjour"), Event(8, "executor_completed", "fr", "") }
        }));
        var rows = InvocationTimeline.Entries(GraphEvidence.From(journal));
        Assert.Null(rows[0].Start);
        Assert.Null(rows[0].Call.Duration);
        Assert.Equal("translation-aggregate #2 · fr", rows[1].Label);
        Assert.Equal(TimeSpan.FromSeconds(3), rows[1].Call.Duration);
        Assert.Equal(TimeSpan.FromSeconds(1), rows[1].Call.StartOffset);
        Assert.Equal(4000d, InvocationTimeline.TimeScale(rows, null));
        Assert.Contains("bonjour", rows[1].Details);
        Assert.False(rows[1].Concurrent);
    }
}
