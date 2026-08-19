namespace MafPlayground.AI.Agents.RepositoryHelpAgent;

public interface IRepositoryCliCommandCatalog
{
    IReadOnlyList<string> CommandPaths { get; }

    RepositoryCliCommand? Find(string commandPath);
}

public sealed record RepositoryCliCommand(
    string CommandPath,
    string Invocation,
    string Description);
