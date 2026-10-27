namespace MafPlayground.Studio.Contracts;

public sealed record StudioComparisonRequest(string HostInstanceId, string BaselineRunId, string CandidateRunId);
public sealed record StudioComparison(string HostInstanceId, StudioComparisonRun Baseline, StudioComparisonRun Candidate,
    IReadOnlyList<StudioValueChange> Changes, StudioMetricDelta Duration, StudioMetricDelta ModelAttempts,
    StudioMetricDelta InputTokens, StudioMetricDelta OutputTokens, StudioMetricDelta Cost,
    string InvocationMatchingRule, IReadOnlyList<StudioInvocationMatch> Invocations, bool Truncated, IReadOnlyList<string> Limitations);
public sealed record StudioComparisonRun(string RunId, string EntityId, string ProfileId, StudioRunStatus Status, StudioOutcomeClassification Outcome,
    StudioExecutionProfileSnapshot? Profile, bool CaptureComplete, bool JournalHasGap, bool HasRedactions, StudioComparisonMetrics Metrics, string? ErrorCategory, IReadOnlyList<StudioComparisonError> Errors, bool ErrorCoverageComplete);
public sealed record StudioComparisonMetrics(int ObservedModelAttempts, double? InputTokens, double? OutputTokens, double? Cost,
    string? Currency, int InputTokenCoverage, int OutputTokenCoverage, int CostCoverage);
public sealed record StudioMetricDelta(double? Baseline, double? Candidate, double? Delta);
public sealed record StudioValueChange(string Path, string Kind, string? Baseline, string? Candidate, bool PreviewTruncated, int? TextOffset = null);
public sealed record StudioInvocationMatch(string? ExecutorId, int Occurrence, string? BaselineInvocationId, string? CandidateInvocationId);

/// <summary>Bounded retained error preview; null correlation remains unknown.</summary>
public sealed record StudioComparisonError(string Kind, string? ExecutorId, string? InvocationId, StudioCaptureState State, string Detail);
