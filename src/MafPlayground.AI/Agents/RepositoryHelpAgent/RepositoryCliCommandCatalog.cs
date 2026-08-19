namespace MafPlayground.AI.Agents.RepositoryHelpAgent;

public interface IRepositoryCliCommandCatalog
{
    IReadOnlyList<string> CommandPaths { get; }

    RepositoryCliCommand? Find(string commandPath);

    IReadOnlyList<RepositoryCliCommand> Search(
        string request,
        int maxResults = 3);
}

public sealed record RepositoryCliCommand(
    string CommandPath,
    string Invocation,
    string Description);
