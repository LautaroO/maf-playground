using System.Text.Json;

namespace MafPlayground.CLI.Tui;

internal sealed record CatalogRun(string Entity, DateTimeOffset Submitted, string Status, TimeSpan? Duration,
    long Events, Action Open, string? RunId = null)
{
    public bool SameDisplayAs(CatalogRun other) => RunId == other.RunId && Entity == other.Entity && Submitted == other.Submitted &&
        Status == other.Status && Events == other.Events && Duration == other.Duration;

    public bool Active => Status is "Queued" or "Running";
    public bool Failed => Status is "Failed" or "Timed out" or "Partially failed" or "Partial result";
    public string DurationLabel => Duration switch
    {
        null => "—",
        { TotalSeconds: < 1 } d => $"{d.TotalMilliseconds:0}ms",
        { TotalMinutes: < 1 } d => $"{d.TotalSeconds:0.0}s",
        { } d => $"{(long)d.TotalMinutes}m{d.Seconds:00}s"
    };

    public static IEnumerable<CatalogRun> FromStudio(JsonElement snapshots, string instance, string profile,
        DateTimeOffset now, Action<string, string> open)
    {
        foreach (var snapshot in snapshots.Items())
        {
            var request = snapshot.Field("request");
            if (snapshot.Str("hostInstanceId") != instance || request.Str("profileId") != profile ||
                request.Str("entityId") is not { } entity || snapshot.Str("runId") is not { } id ||
                !DateTimeOffset.TryParse(snapshot.Str("submittedAt"), out var submitted)) continue;
            var status = snapshot.RunStatus();
            TimeSpan? elapsed = null;
            if (DateTimeOffset.TryParse(snapshot.Str("startedAt"), out var started))
            {
                if (DateTimeOffset.TryParse(snapshot.Str("completedAt"), out var end)) elapsed = end - started;
                else if (status == "Running") elapsed = now - started;
            }
            yield return new(entity, submitted, status, elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed,
                snapshot.Number("totalEvents"), () => open(entity, id), id);
        }
    }
}

internal sealed partial class InspectorScreen
{
    internal IEnumerable<CatalogRun> CatalogRuns(Action show)
    {
        if (_run.Status != "Ready")
            yield return new(_entity.Id, _started, _run.Status, _run.Elapsed, _run.Events.Count, show);
        foreach (var capture in _history)
        {
            if (capture.Started == _started && capture.Entity == _entity.Id) continue;
            yield return new(capture.Entity, capture.Started, capture.Status, capture.Elapsed, capture.Events.Length, () =>
            {
                // A running inspector owns the live update queue until execution settles.
                if (!Pending.IsCompleted || !_updates.IsEmpty)
                {
                    ShowText($"{capture.Entity} · {capture.Started.ToLocalTime():g} · {capture.Status}",
                        $"RESULT\n{capture.Result}\n\nINPUT\n{capture.Input}\n\nEVENTS\n{string.Join('\n', capture.Events)}");
                    return;
                }
                show();
                ArchiveCurrent();
                Load(capture);
                SetNeedsDraw();
            });
        }
    }
}
