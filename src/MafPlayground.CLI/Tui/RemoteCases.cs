using System.Text.Json;

namespace MafPlayground.CLI.Tui;

internal sealed partial class RemoteWorkspace
{
    private readonly CaseLibrary _cases = new();

    public void OpenCases()
    {
        if (_busy) { Notice("A Studio request is pending."); return; }
        var draft = _draft;
        _cases.Open(draft.Id, draft.Profile + " · without session", _input, json =>
        {
            _input = json;
            _requestId = Guid.NewGuid().ToString();
        }, () =>
        {
            if (draft.Entity.Field("isAvailable").ValueKind == JsonValueKind.True) return true;
            Notice(draft.Entity.Str("unavailableReason") ?? "Entity unavailable.");
            return false;
        }, (item, token) => ExecuteCaseAsync(draft, item, token), _token);
    }

    public void CompareModels()
    {
        if (_busy) { Notice("A Studio request is pending."); return; }
        if (_profiles.Length < 2) { Notice("Two Studio execution profiles are required."); return; }
        var entity = _draft.Id;
        int first = Pick("Baseline execution profile", _profiles);
        if (first < 0) return;
        string baseline = _profiles[first];
        var others = _profiles.Where(profile => profile != baseline).ToArray();
        int second = Pick("Candidate execution profile", others);
        if (second < 0) return;
        string candidate = others[second];
        Request(() => Task.WhenAll(RemoteDraft.LoadAsync(_client, baseline, entity, _token),
            RemoteDraft.LoadAsync(_client, candidate, entity, _token)), drafts =>
        {
            if (drafts.FirstOrDefault(d => d.Entity.Field("isAvailable").ValueKind != JsonValueKind.True) is { } missing)
            {
                Notice($"{entity} is unavailable for {missing.Profile}.");
                return;
            }
            CaseComparison.Open(entity, baseline, candidate, (profile, item, ct) =>
                ExecuteCaseAsync(profile == baseline ? drafts[0] : drafts[1], item, ct), (a, b) =>
            {
                var left = a.Remote!;
                var right = b.Remote!;
                string summary = $"{baseline}   {CaseComparison.Result(a)}\n{left.Snapshot.Field("result").Pretty()}\n\n" +
                    $"{candidate}   {CaseComparison.Result(b)}\n{right.Snapshot.Field("result").Pretty()}";
                GraphInspectionDialog.Compare(GraphEvidence.From(left), GraphEvidence.From(right), summary,
                    truncated: left.HasGap || right.HasGap);
            }, _token);
        });
    }

    private async Task<CaseOutcome> ExecuteCaseAsync(RemoteDraft draft, StudioCase item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Let an in-flight submit return its run ID so cancellation can reach the server.
        var receipt = await _client.StartRunAsync(Guid.NewGuid().ToString(), draft.Id, draft.Profile, item.Input, null, _token).ConfigureAwait(false);
        if (receipt.Str("hostInstanceId") != _instance) throw new InvalidDataException("Studio restarted. Reconnect before running cases.");
        var journal = new RemoteJournal(receipt.GetProperty("runId").GetString()!);
        try
        {
            while (!journal.Finished)
            {
                var snapshot = await _client.RunAsync(journal.Id, token).ConfigureAwait(false);
                if (snapshot.Str("hostInstanceId") != _instance) throw new InvalidDataException("Studio restarted during this case.");
                var page = await _client.EventPageAsync(journal.Id, journal.Cursor, 256, token).ConfigureAwait(false);
                journal.Apply(snapshot, page);
                if (!journal.Finished && journal.Cursor >= snapshot.Number("latestSequence"))
                    await Task.Delay(350, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _client.CancelAsync(journal.Id, timeout.Token).ConfigureAwait(false);
            return new(item.Name, [new("Cancelled")], () => SelectRun(journal.Id));
        }
        var (inputTokens, outputTokens) = journal.ModelTokens();
        TimeSpan? duration = DateTimeOffset.TryParse(journal.Snapshot.Str("startedAt"), out var start) &&
            DateTimeOffset.TryParse(journal.Snapshot.Str("completedAt"), out var end) && end >= start ? end - start : null;
        var (failures, target) = EvaluateCase(journal, item.Checks);
        return new(item.Name, failures, () =>
        {
            _run = journal;
            _pollStopped = true;
            ViewPanel(target.ValueKind == JsonValueKind.Object ? Panel.Events : Panel.Result);
            if (target.ValueKind != JsonValueKind.Object) return;
            int row = Array.FindIndex(_rows, r => r.Key == "event-" + target.Number("sequence"));
            if (row >= 0) _list.SelectedItem = row;
        })
        { Remote = journal, Duration = duration, InputTokens = inputTokens, OutputTokens = outputTokens };
    }

    internal static (List<CaseFailure> Failures, JsonElement Target) EvaluateCase(RemoteJournal journal, CaseChecks checks)
    {
        var calls = journal.Events.Where(e => e.Str("kind") == "executor_started" && e.Str("executorId") is not null && e.Str("invocationId") is not null)
            .DistinctBy(e => (e.Str("executorId"), e.Str("invocationId"))).ToArray();
        var nodes = calls.Select(e => e.Str("executorId")!).ToArray();
        bool observed = nodes.Length > 0 && (nodes.Contains(checks.Node) ||
            journal.Snapshot.Field("topology").Field("nodes").Items().Any(n => n.Str("id") == checks.Node));
        var failures = checks.Evaluate(journal.Snapshot.RunStatus(), journal.Snapshot.Field("result"), nodes,
            observed && !journal.HasGap && journal.Snapshot.Field("captureComplete").ValueKind == JsonValueKind.True);
        var excess = failures.FirstOrDefault(f => f.Node is not null);
        var target = excess is null ? RemoteJournal.Failures(journal.Events.ToArray()).FirstOrDefault()
                : calls.Where(e => e.Str("executorId") == excess.Node).ElementAtOrDefault(excess.Index);
        return (failures, target);
    }
}
