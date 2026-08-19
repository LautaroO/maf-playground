using System.CommandLine;

namespace MafPlayground.CLI.Commands;

public sealed class AgentCommand : ICliCommand
{
    public int Order => 100;

    Command ICliCommand.Create() => Create();

    public static Command Create(
        Func<BasicAgentCommandOptions, CancellationToken, Task<int>>? runBasicAgentAsync = null,
        Func<BasicRagAgentCommandOptions, CancellationToken, Task<int>>? runBasicRagAgentAsync = null,
        Func<RepositoryHelpAgentCommandOptions, CancellationToken, Task<int>>? runRepositoryHelpAgentAsync = null)
    {
        Command command = new("agent", "Run and test agents.");
        command.Subcommands.Add(BasicAgentCommand.Create(runBasicAgentAsync));
        command.Subcommands.Add(BasicRagAgentCommand.Create(runBasicRagAgentAsync));
        command.Subcommands.Add(RepositoryHelpAgentCommand.Create(runRepositoryHelpAgentAsync));
        return command;
    }
}
