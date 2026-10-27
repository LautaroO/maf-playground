using System.Text.Json;

namespace MafPlayground.Studio.Contracts;

public sealed record StudioEvent(string RunId, long Sequence, DateTimeOffset Timestamp, string Kind, string? ExecutorId, string? InvocationId, string? SpanId, JsonElement Data, StudioCaptureState CaptureState = StudioCaptureState.Available);
public sealed record StudioEventPage(IReadOnlyList<StudioEvent> Events, long NextCursor, long EarliestRetainedSequence, bool HasGap);
