using System.CommandLine;
using MafPlayground.AI.Agents.RepositoryHelpAgent;

namespace MafPlayground.CLI.Documentation;

public sealed class SystemCommandLineRepositoryCliCommandCatalog
    : IRepositoryCliCommandCatalog
{
    private readonly IReadOnlyDictionary<string, RepositoryCliCommand> _commands;

    public SystemCommandLineRepositoryCliCommandCatalog(RootCommand rootCommand)
    {
        ArgumentNullException.ThrowIfNull(rootCommand);
        Dictionary<string, RepositoryCliCommand> commands =
            new(StringComparer.OrdinalIgnoreCase);
        AddCommands(commands, rootCommand, []);
        _commands = commands;
        CommandPaths = commands.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> CommandPaths { get; }

    public RepositoryCliCommand? Find(string commandPath)
    {
        if (string.IsNullOrWhiteSpace(commandPath))
        {
            return null;
        }

        string normalized = string.Join(
            ' ',
            commandPath.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return _commands.GetValueOrDefault(normalized);
    }

    private static void AddCommands(
        IDictionary<string, RepositoryCliCommand> entries,
        Command parent,
        IReadOnlyList<string> parentPath)
    {
        foreach (Command command in parent.Subcommands.Where(command => !command.Hidden))
        {
            string[] path = [.. parentPath, command.Name];
            string commandPath = string.Join(' ', path);
            entries.Add(
                commandPath,
                new RepositoryCliCommand(
                    commandPath,
                    RepositoryHelpCliReferenceGenerator.BuildInvocation(command, path),
                    command.Description ?? string.Empty));
            AddCommands(entries, command, path);
        }
    }
}
