using System.CommandLine;

namespace MafPlayground.CLI.Commands;

public sealed class WorkflowCommand : ICliCommand
{
    public int Order => 200;

    Command ICliCommand.Create() => Create();

    public static Command Create(
        Func<TranslateWorkflowCommandOptions, CancellationToken, Task<int>>? runTranslateAsync = null)
    {
        Command command = new("workflow", "Run and test workflows.");
        command.Subcommands.Add(TranslateWorkflowCommand.Create(runTranslateAsync));
        return command;
    }
}
