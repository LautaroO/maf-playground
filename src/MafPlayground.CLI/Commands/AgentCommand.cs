using System.CommandLine;

namespace MafPlayground.CLI.Commands;

public static class AgentCommand
{
    public static Command Create(
        Func<BasicAgentCommandOptions, CancellationToken, Task<int>>? runBasicAgentAsync = null)
    {
        Command command = new("agent", "Run and test agents.");
        command.Subcommands.Add(BasicAgentCommand.Create(runBasicAgentAsync));
        return command;
    }
}
