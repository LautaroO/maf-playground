using MafPlayground.Retrieval;

namespace MafPlayground.Evals.RepositoryHelp;

internal sealed class FixtureKnowledgeSearchFactory(
    IReadOnlyList<KnowledgeSearchResult> results) : IKnowledgeSearchFactory
{
    public IKnowledgeSearch Create(
        KnowledgeBaseId knowledgeBaseId,
        KnowledgeSearchOptions searchOptions) => new FixtureKnowledgeSearch(results);
}

internal sealed class FixtureKnowledgeSearch(
    IReadOnlyList<KnowledgeSearchResult> results) : IKnowledgeSearch
{
    public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(results);
    }
}
