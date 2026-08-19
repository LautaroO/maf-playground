namespace MafPlayground.AI.Agents.BasicRagAgent;

public sealed record RagAnswerDraft(
    bool InsufficientEvidence,
    IReadOnlyList<RagClaim>? Claims);

public sealed record RagClaim(
    string? Text,
    IReadOnlyList<string>? CitationIds);

public sealed record RagEvidence(
    string CitationId,
    string Text,
    string Citation,
    double Similarity,
    IReadOnlyList<string>? RequiredInlineCode = null);

public sealed record RagSearchToolResult(
    IReadOnlyList<RagEvidence> Evidence,
    string? Message);

public sealed record RagAnswerValidationResult(
    bool IsValid,
    IReadOnlyList<string> Issues);
