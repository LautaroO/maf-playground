namespace MafPlayground.Studio.Contracts;

public sealed record StudioMcpPage<T>(IReadOnlyList<T> Items, int? NextCursor);
public sealed record StudioMcpSpanPage(IReadOnlyList<StudioSpan> Spans, long NextCursor, long EarliestRetainedSequence, bool HasGap);
