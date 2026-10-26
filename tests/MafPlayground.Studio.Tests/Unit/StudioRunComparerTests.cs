using System.Text.Json;
using MafPlayground.Studio.Comparison;
using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioRunComparerTests
{
    [Fact]
    public void Object_order_is_ignored_but_arrays_and_text_are_structural_changes()
    {
        var a = Snapshot("a", """{"b":2,"a":1,"items":[1,2],"text":"hello"}""");
        var b = Snapshot("b", """{"text":"hello","items":[1,2],"a":1,"b":2}""");
        Assert.Empty(Compare(a, b).Changes);
        var changed = Compare(a, b with { Result = Json("""{"a":1,"b":2,"items":[2,1],"text":"world"}""") });
        Assert.Equal(new[] { "/result/items/0", "/result/items/1", "/result/text" }, changed.Changes.Select(x => x.Path));
        Assert.Equal("text", changed.Changes.Last().Kind);
    }
    [Fact]
    public void Profiles_outcomes_and_duration_change_without_inventing_missing_usage_or_cost()
    {
        var a = Snapshot("a", "null");
        var b = Snapshot("b", "null") with { Profile = new("alternate", "local:other", null, null), Outcome = StudioOutcomeClassification.PartiallyFailed, CompletedAt = a.CompletedAt!.Value.AddSeconds(2) };
        var result = Compare(a, b);
        Assert.Contains(result.Changes, x => x.Path == "/profile/id");
        Assert.Equal(2000, result.Duration.Delta);
        Assert.Null(result.InputTokens.Delta);
        Assert.Null(result.Cost.Baseline);
        Assert.Null(result.Cost.Candidate);
        Assert.Equal(StudioOutcomeClassification.PartiallyFailed, result.Candidate.Outcome);
    }
    [Fact]
    public void Repeated_invocations_match_exact_executor_occurrence_and_keep_unmatched_and_unknown_ids()
    {
        var result = new StudioRunComparer().Compare(Snapshot("a", "null"), Snapshot("b", "null"),
            [Event(1, "a1", "executor"), Event(2, "a1", "executor"), Event(3, "a2", "executor"), Event(4, "a3", null)],
            [Event(1, "b1", "executor")]);
        Assert.Equal(3, result.Invocations.Count);
        Assert.Equal("b1", result.Invocations[0].CandidateInvocationId);
        Assert.Null(result.Invocations[1].CandidateInvocationId);
        Assert.Null(result.Invocations[2].ExecutorId);
        Assert.Contains("occurrence", result.InvocationMatchingRule);
        Assert.Contains("causal", result.InvocationMatchingRule);
    }
    [Fact]
    public void Usage_uses_deduplicated_model_observations_only_and_partial_coverage_is_unknown()
    {
        var model = Span("one", "MafPlayground.Studio.Model", 10, 5, 1, "USD");
        var native = Span("native", "Microsoft.Agents.AI", 100, 100, 10, "USD");
        var result = new StudioRunComparer().Compare(Snapshot("a", "null"), Snapshot("b", "null"), [model, model, native], [model, Span("two", "MafPlayground.Studio.Model", null, 0, null, null)]);
        Assert.Equal(1, result.ModelAttempts.Baseline);
        Assert.Equal(2, result.ModelAttempts.Candidate);
        Assert.Equal(10, result.InputTokens.Baseline);
        Assert.Null(result.InputTokens.Candidate);
        Assert.Null(result.Cost.Delta);
        Assert.Equal(1, result.Baseline.Metrics.InputTokenCoverage);
    }
    [Fact]
    public void Capture_gaps_and_missing_output_are_explicit_and_never_equate_to_json_null()
    {
        var result = Compare(Snapshot("a", "null") with { CaptureComplete = false, EarliestSequence = 3, Result = null }, Snapshot("b", "null"));
        Assert.False(result.Baseline.CaptureComplete);
        Assert.True(result.Baseline.JournalHasGap);
        Assert.Contains(result.Changes, x => x.Path == "/result" && x.Kind == "unavailable");
        Assert.Null(result.InputTokens.Delta);
    }
    [Fact]
    public void Long_text_and_many_changes_are_bounded_and_disclosed()
    {
        var a = Snapshot("a", JsonSerializer.Serialize(new string('a', 200_000)));
        var b = Snapshot("b", JsonSerializer.Serialize(new string('b', 200_000)));
        var text = Compare(a, b);
        Assert.True(Assert.Single(text.Changes).PreviewTruncated);
        Assert.True(text.Changes[0].Baseline!.Length < 300);
        var many = Compare(Snapshot("a", "[]"), Snapshot("b", JsonSerializer.Serialize(Enumerable.Range(0, 300))));
        Assert.True(many.Truncated);
        Assert.NotEmpty(many.Limitations);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(many).Length < 128 * 1024);
    }
    [Fact]
    public void Text_preview_locates_changes_after_a_long_shared_prefix()
    {
        var result = Compare(Snapshot("a", JsonSerializer.Serialize(new string('x', 100_000) + "old")), Snapshot("b", JsonSerializer.Serialize(new string('x', 100_000) + "new")));
        var change = Assert.Single(result.Changes);
        Assert.Equal(100_000, change.TextOffset);
        Assert.Contains("old", change.Baseline);
        Assert.Contains("new", change.Candidate);
    }
    [Theory]
    [InlineData("😀", "😁", 0)]
    [InlineData("same😀", "same😁", 4)]
    public void Supplementary_text_differences_keep_complete_scalars_after_serialization(string baseline, string candidate, int offset)
    {
        var result = Compare(Snapshot("a", JsonSerializer.Serialize(baseline)), Snapshot("b", JsonSerializer.Serialize(candidate)));
        var change = Assert.Single(result.Changes);
        var roundTrip = JsonSerializer.Deserialize<StudioComparison>(JsonSerializer.SerializeToUtf8Bytes(result))!;
        var received = Assert.Single(roundTrip.Changes);
        Assert.Equal(offset, received.TextOffset);
        Assert.Equal("😀", received.Baseline);
        Assert.Equal("😁", received.Candidate);
        Assert.Equal(change.Baseline, received.Baseline);
        Assert.Equal(change.Candidate, received.Candidate);
    }
    [Fact]
    public void Preview_limit_does_not_split_a_supplementary_scalar()
    {
        var result = Compare(Snapshot("a", JsonSerializer.Serialize(new string('x', 159) + "😀tail")),
            Snapshot("b", JsonSerializer.Serialize(new string('y', 159) + "😁tail")));
        var received = JsonSerializer.Deserialize<StudioComparison>(JsonSerializer.SerializeToUtf8Bytes(result))!;
        var change = Assert.Single(received.Changes);
        Assert.Equal(new string('x', 159) + "…", change.Baseline);
        Assert.Equal(new string('y', 159) + "…", change.Candidate);
        Assert.True(change.PreviewTruncated);
        Assert.Equal(0, change.TextOffset);
    }
    [Fact]
    public void Zero_usage_is_known_but_missing_usage_and_currency_changes_are_not_comparable()
    {
        var result = new StudioRunComparer().Compare(Snapshot("a", "null"), Snapshot("b", "null"),
            [Span("a", "MafPlayground.Studio.Model", 0, 2, 0, "USD")], [Span("b", "MafPlayground.Studio.Model", 5, null, 1, "EUR")]);
        Assert.Equal(0, result.InputTokens.Baseline);
        Assert.Equal(5, result.InputTokens.Delta);
        Assert.Null(result.OutputTokens.Delta);
        Assert.Null(result.Cost.Delta);
        Assert.Equal(0, result.Cost.Baseline);
        var gap = new StudioRunComparer().Compare(Snapshot("a", "null"), Snapshot("b", "null"),
            [Span("a", "MafPlayground.Studio.Model", 0, 2, 0, "USD")], [], baselineGap: true);
        Assert.Null(gap.InputTokens.Baseline);
        Assert.True(gap.Baseline.JournalHasGap);
    }
    [Fact]
    public void Removed_properties_and_requested_profile_changes_are_visible_when_effective_profile_is_missing()
    {
        var a = Snapshot("a", "{\"old\":1}") with { Profile = null };
        var b = Snapshot("b", "{\"new\":2}") with { Profile = null, Request = a.Request with { ProfileId = "alternate" } };
        var result = Compare(a, b);
        Assert.Contains(result.Changes, x => x.Path == "/result/old" && x.Kind == "removed");
        Assert.Contains(result.Changes, x => x.Path == "/result/new" && x.Kind == "added");
        Assert.Contains(result.Changes, x => x.Path == "/profileId");
    }
    [Fact]
    public async Task Service_rejects_pending_runs_and_reads_real_retained_journal_gaps_without_creating_runs()
    {
        await using var host = await RunHarness.CreateAsync(options => options with { MaxEventsPerRun = 3 });
        var receipt = await host.SubmitAsync("compare");
        var job = await host.NextAsync();
        var service = new StudioComparisonService(host.Service, host.Service, new());
        var request = new StudioComparisonRequest(receipt.HostInstanceId, receipt.RunId, receipt.RunId);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CompareAsync(request, default).AsTask());
        for (int i = 0; i < 12; i++) host.Record(receipt.RunId, new { index = i });
        job.Complete(); await host.TerminalAsync(receipt.RunId);
        var comparison = await service.CompareAsync(request, default);
        Assert.True(comparison.Baseline.JournalHasGap);
        Assert.False(comparison.Baseline.CaptureComplete);
        Assert.Null(comparison.InputTokens.Baseline);
        Assert.Equal(1, host.Count);
    }
    [Fact]
    public void Redaction_is_explicit_but_does_not_invent_lost_journal_history()
    {
        var redacted = Span("a", "MafPlayground.Studio.Model", 10, 5, 1, "USD") with { CaptureState = StudioCaptureState.Redacted };
        var result = new StudioRunComparer().Compare(Snapshot("a", "null"), Snapshot("b", "null"), [redacted], []);
        Assert.True(result.Baseline.CaptureComplete);
        Assert.True(result.Baseline.HasRedactions);
        Assert.False(result.Baseline.JournalHasGap);
        Assert.Null(result.InputTokens.Baseline);
    }
    [Fact]
    public void Cross_host_and_running_snapshots_are_rejected_and_cancellation_is_observed()
    {
        Assert.Throws<ArgumentException>(() => Compare(Snapshot("a", "null"), Snapshot("b", "null") with { HostInstanceId = "other" }));
        Assert.Throws<InvalidOperationException>(() => Compare(Snapshot("a", "null"), Snapshot("b", "null") with { Status = StudioRunStatus.Running }));
        Assert.Throws<OperationCanceledException>(() => new StudioRunComparer().Compare(Snapshot("a", "null"), Snapshot("b", "null"), [], [], cancellationToken: new(true)));
    }
    private static StudioComparison Compare(StudioRunSnapshot a, StudioRunSnapshot b) => new StudioRunComparer().Compare(a, b, [], []);
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static StudioRunSnapshot Snapshot(string id, string result) => new("host", id, new(id, "generic", "default", Json("{}"), null), StudioRunStatus.Completed, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), Json(result), StudioOutcomeClassification.Succeeded, null, null, 0, 0, true, 1, 0, new("default", null, null, null));
    private static StudioEvent Event(long sequence, string invocation, string? executor) => new("run", sequence, DateTimeOffset.UnixEpoch, "executor_input", executor, invocation, null, Json("{}"));
    private static StudioEvent Span(string id, string source, long? input, long? output, double? cost, string? currency)
    {
        var payload = new StudioPayload("p", "application/json", Json("{}"), StudioCaptureState.Available, null);
        return new("run", 1, DateTimeOffset.UnixEpoch, "span", null, null, id, JsonSerializer.SerializeToElement(new StudioSpan("trace", id, "", source, "call", DateTimeOffset.UnixEpoch, TimeSpan.Zero, "Ok", null, null, "unknown", input, output, cost, currency, payload, payload, payload)));
    }
}
