using System.ComponentModel;
using MafPlayground.AI.Agents.BasicRagAgent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Agents.RepositoryHelpAgent;

internal sealed class RepositoryCliContextProvider(
    IRepositoryCliCommandCatalog commandCatalog,
    RagInvocationContextAccessor invocationContextAccessor) : AIContextProvider
{
    private const string Citation =
        "[MafPlayground CLI reference, source: cli-reference.md]";

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AIFunction getCommand = AIFunctionFactory.Create(
            ([Description("Exact CLI command path, for example 'devui' or 'rag ingest'.")] string commandPath) =>
            {
                RepositoryCliCommand? command = commandCatalog.Find(commandPath);
                if (command is null)
                {
                    return new RepositoryCliCommandToolResult(
                        null,
                        $"Unknown command path. Available paths: {string.Join(", ", commandCatalog.CommandPaths)}.");
                }

                string evidenceText = string.IsNullOrWhiteSpace(command.Description)
                    ? $"Command `{command.CommandPath}` uses the exact invocation `{command.Invocation}`."
                    : $"Command `{command.CommandPath}` uses the exact invocation `{command.Invocation}`. {command.Description}";
                RagEvidence evidence = invocationContextAccessor.Current.AddEvidence(
                    evidenceText,
                    Citation,
                    similarity: 1);
                return new RepositoryCliCommandToolResult(evidence, null);
            },
            name: "get_cli_command",
            description: "Gets the exact invocation for one repository CLI command from the live System.CommandLine catalog.");

        return ValueTask.FromResult(new AIContext
        {
            Instructions =
                "When exact CLI syntax is needed, call get_cli_command with one available command path. " +
                $"Available command paths: {string.Join(", ", commandCatalog.CommandPaths)}.",
            Tools = [getCommand],
        });
    }
}

internal sealed record RepositoryCliCommandToolResult(
    RagEvidence? Evidence,
    string? Error);
