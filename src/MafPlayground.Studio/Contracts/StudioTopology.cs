namespace MafPlayground.Studio.Contracts;

public enum StudioTopologyStatus { Available, Unavailable }

public sealed record StudioTopology(
    string EntityId,
    string ProfileId,
    StudioTopologyStatus Status,
    string? UnavailableReason,
    string? StartNodeId,
    IReadOnlyList<StudioTopologyNode> Nodes,
    IReadOnlyList<StudioTopologyEdge> Edges,
    string? LabelsUnavailableReason,
    string? NativeMermaid);

public sealed record StudioTopologyNode(string Id, string Label, bool IsStart);

public sealed record StudioTopologyEdge(
    string Id,
    string Kind,
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<string> TargetIds,
    bool IsConditional,
    string? Label);
