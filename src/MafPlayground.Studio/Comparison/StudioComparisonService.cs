using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using MafPlayground.Studio.Http;

namespace MafPlayground.Studio.Comparison;

public sealed class StudioComparisonService(IStudioRunService runs, StudioRunService host, StudioRunComparer comparer)
{
    public async ValueTask<StudioComparison> CompareAsync(StudioComparisonRequest request, CancellationToken cancellationToken)
    {
        ValidateId(request.HostInstanceId); ValidateId(request.BaselineRunId); ValidateId(request.CandidateRunId);
        if (request.HostInstanceId != host.HostInstanceId) throw new ArgumentException("Cross-host comparison is unavailable.");
        var baseline = await Required(request.BaselineRunId, cancellationToken);
        var candidate = await Required(request.CandidateRunId, cancellationToken);
        if (!baseline.IsTerminal || !candidate.IsTerminal) throw new StudioEndpoints.StudioResultPendingException();
        var a = await Read(baseline, cancellationToken);
        var b = await Read(candidate, cancellationToken);
        // An eviction during inspection invalidates the comparison; do not silently return stale snapshots.
        _ = await Required(baseline.RunId, cancellationToken);
        _ = await Required(candidate.RunId, cancellationToken);
        return comparer.Compare(baseline, candidate, a.Events, b.Events, a.Gap, b.Gap, cancellationToken);
    }
    private async ValueTask<StudioRunSnapshot> Required(string id, CancellationToken token)
    {
        var snapshot = await runs.GetAsync(id, token) ?? throw new KeyNotFoundException();
        if (snapshot.HostInstanceId != host.HostInstanceId) throw new ArgumentException("Cross-host comparison is unavailable.");
        return snapshot;
    }
    private async ValueTask<(IReadOnlyList<StudioEvent> Events, bool Gap)> Read(StudioRunSnapshot snapshot, CancellationToken token)
    {
        List<StudioEvent> events = [];
        bool gap = false;
        long after = 0;
        // Freeze the horizon at the snapshot; late spans remain visible on a subsequent comparison.
        while (after < snapshot.LatestSequence && events.Count < StudioRunComparer.MaxEvents)
        {
            var page = await runs.ReadEventsAsync(snapshot.RunId, after, Math.Min(100, StudioRunComparer.MaxEvents - events.Count), token);
            gap |= page.HasGap;
            events.AddRange(page.Events.Where(x => x.Sequence <= snapshot.LatestSequence));
            if (page.NextCursor <= after) { gap = true; break; }
            after = page.NextCursor;
        }
        return (events, gap || after < snapshot.LatestSequence);
    }
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl)) throw new ArgumentException("Invalid comparison identity.");
    }
}
