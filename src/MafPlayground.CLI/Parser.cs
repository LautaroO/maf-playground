using System.CommandLine;
using System.Reflection;
using MafPlayground.CLI.Commands;

namespace MafPlayground.CLI;

public static class Parser
{
    public static RootCommand CreateRootCommand() =>
        CreateRootCommand(DiscoverCommands(typeof(Parser).Assembly));

    internal static RootCommand CreateRootCommand(IEnumerable<ICliCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        RootCommand rootCommand = new("Microsoft Agent Framework playground CLI");
        foreach (ICliCommand command in commands.OrderBy(command => command.Order))
        {
            rootCommand.Subcommands.Add(command.Create());
        }

        return rootCommand;
    }

    private static IEnumerable<ICliCommand> DiscoverCommands(Assembly assembly) =>
        assembly.DefinedTypes
            .Where(type =>
                type is { IsAbstract: false, IsInterface: false } &&
                typeof(ICliCommand).IsAssignableFrom(type))
            .Select(type =>
                Activator.CreateInstance(type.AsType()) as ICliCommand
                ?? throw new InvalidOperationException(
                    $"CLI command type '{type.FullName}' must have a public parameterless constructor."));
}
