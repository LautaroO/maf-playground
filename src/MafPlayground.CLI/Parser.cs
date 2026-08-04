using System.CommandLine;
using MafPlayground.CLI.Commands;

namespace MafPlayground.CLI;

public static class Parser
{
    public static RootCommand CreateRootCommand(
        Func<BasicAgentCommandOptions, CancellationToken, Task<int>>? runBasicAgentAsync = null,
        Func<DevUICommandOptions, CancellationToken, Task<int>>? runDevUIAsync = null,
        Func<TranslateWorkflowCommandOptions, CancellationToken, Task<int>>? runTranslateAsync = null,
        Func<BasicRagAgentCommandOptions, CancellationToken, Task<int>>? runBasicRagAgentAsync = null,
        Func<RagMigrateCommandOptions, CancellationToken, Task<int>>? runRagMigrateAsync = null,
        Func<RagIngestCommandOptions, CancellationToken, Task<int>>? runRagIngestAsync = null,
        Func<InspectCommandOptions, CancellationToken, Task<int>>? runInspectAsync = null)
    {
        RootCommand rootCommand = new("Microsoft Agent Framework playground CLI");
        rootCommand.Subcommands.Add(AgentCommand.Create(runBasicAgentAsync, runBasicRagAgentAsync));
        rootCommand.Subcommands.Add(WorkflowCommand.Create(runTranslateAsync));
        rootCommand.Subcommands.Add(RagCommand.Create(runRagMigrateAsync, runRagIngestAsync));
        rootCommand.Subcommands.Add(DevUICommand.Create(runDevUIAsync));
        rootCommand.Subcommands.Add(InspectCommand.Create(runInspectAsync));
        return rootCommand;
    }
}
