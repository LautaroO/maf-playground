using System.Text.Json;

namespace MafPlayground.Studio.Contracts;

public enum StudioRunStatus { Queued, Running, Completed, PartiallyFailed, Failed, Cancelled, TimedOut }
public enum StudioOutcomeClassification { Unclassified, Succeeded, PartiallyFailed, Failed }
public sealed record StudioRunSnapshot(
    string HostInstanceId, string RunId, StudioRunRequest Request, StudioRunStatus Status,
    bool CancellationRequested, DateTimeOffset SubmittedAt, DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt, JsonElement? Result, StudioOutcomeClassification Outcome,
    StudioTopology? Topology, string? ErrorCategory, long TotalEvents, long CapturedBytes,
    bool CaptureComplete, long EarliestSequence, long LatestSequence)
{
    public bool IsTerminal => Status is not (StudioRunStatus.Queued or StudioRunStatus.Running);
}
