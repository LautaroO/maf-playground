using System.CommandLine;

namespace MafPlayground.CLI;

public static class Parser
{
    public static RootCommand CreateRootCommand(
        Func<BasicAgentCommandOptions, CancellationToken, Task<int>> runBasicAgentAsync)
    {
        ArgumentNullException.ThrowIfNull(runBasicAgentAsync);

        Option<string?> modelOption = new("--model", "-m")
        {
            Description = "Model selector in provider:model format. Falls back to AI_MODEL."
        };
        Option<string?> promptOption = new("--prompt", "-p")
        {
            Description = "Run one prompt and exit. Omit to start an interactive session."
        };

        Command basicCommand = new("basic", "Run the Basic agent.");
        basicCommand.Options.Add(modelOption);
        basicCommand.Options.Add(promptOption);
        basicCommand.SetAction((parseResult, cancellationToken) =>
            runBasicAgentAsync(
                new BasicAgentCommandOptions(
                    parseResult.GetValue(modelOption),
                    parseResult.GetValue(promptOption)),
                cancellationToken));

        Command agentCommand = new("agent", "Run and test agents.");
        agentCommand.Subcommands.Add(basicCommand);

        RootCommand rootCommand = new("Microsoft Agent Framework playground CLI");
        rootCommand.Subcommands.Add(agentCommand);
        return rootCommand;
    }
}

public sealed record BasicAgentCommandOptions(string? Model, string? Prompt);
