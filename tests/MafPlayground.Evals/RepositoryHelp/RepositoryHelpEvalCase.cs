using MafPlayground.Retrieval;

namespace MafPlayground.Evals.RepositoryHelp;

public sealed record RepositoryHelpEvalCase(
    string Id,
    string Category,
    string Question,
    string ExpectedLanguage,
    IReadOnlyList<string> ExpectedFacts,
    string? ExpectedCommandPath,
    bool ShouldRefuse,
    IReadOnlyList<RepositoryHelpEvalEvidence> Evidence)
{
    public IReadOnlyList<KnowledgeSearchResult> ToSearchResults() => Evidence
        .Select(item => new KnowledgeSearchResult(
            item.SourceId,
            item.Title,
            item.Text,
            item.PageNumber,
            item.SectionName,
            item.Similarity))
        .ToArray();
}

public sealed record RepositoryHelpEvalEvidence(
    string SourceId,
    string Title,
    string Text,
    int? PageNumber,
    string? SectionName,
    double Similarity);
