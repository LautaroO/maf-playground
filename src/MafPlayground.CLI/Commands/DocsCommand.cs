using System.CommandLine;

namespace MafPlayground.CLI.Commands;

public sealed class DocsCommand : ICliCommand
{
    public int Order => 450;

    Command ICliCommand.Create() => Create();

    public static Command Create(
        Func<GenerateCliReferenceCommandOptions, CancellationToken, Task<int>>? generateCliReferenceAsync = null)
    {
        Command command = new("docs", "Generate repository documentation artifacts.");
        command.Subcommands.Add(
            GenerateCliReferenceCommand.Create(generateCliReferenceAsync));
        return command;
    }
}
