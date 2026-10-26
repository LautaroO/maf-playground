namespace MafPlayground.Studio.Contracts;

public sealed record StudioSpan(string TraceId, string SpanId, string ParentSpanId, string Source, string Name,
    DateTimeOffset StartedAt, TimeSpan Duration, string Status, string? ExecutorId, string? InvocationId,
    string? CorrelationUnavailableReason, long? InputTokens, long? OutputTokens, double? Cost, string? Currency,
    StudioPayload Attributes, StudioPayload Events, StudioPayload Links);
