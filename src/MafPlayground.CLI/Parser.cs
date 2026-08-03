using System.CommandLine;
using MafPlayground.CLI.Commands;

namespace MafPlayground.CLI;

public static class Parser
{
    public static RootCommand CreateRootCommand(
        Func<BasicAgentCommandOptions, CancellationToken, Task<int>>? runBasicAgentAsync = null,
        Func<DevUICommandOptions, CancellationToken, Task<int>>? runDevUIAsync = null)
    {
        RootCommand rootCommand = new("Microsoft Agent Framework playground CLI");
        rootCommand.Subcommands.Add(AgentCommand.Create(runBasicAgentAsync));
        rootCommand.Subcommands.Add(DevUICommand.Create(runDevUIAsync));
        return rootCommand;
    }
}
