using System.ComponentModel;
using System.Text;
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
    private const int MaximumToolRequestCharacters = 512;

    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string userRequest = context.AIContext.Messages?
            .LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? string.Empty;
        IEnumerable<RepositoryCliCommand> requestMatches =
            string.IsNullOrWhiteSpace(userRequest)
                ? []
                : commandCatalog.Search(userRequest, maxResults: 1);
        RepositoryCliCommand[] retrievedMatches =
            invocationContextAccessor.Current.Evidence.Values
                .Where(evidence => evidence.Citation.Contains(
                    "source: cli-reference.md",
                    StringComparison.OrdinalIgnoreCase))
                .SelectMany(evidence => commandCatalog.Search(
                    evidence.Text,
                    maxResults: 1))
                .ToArray();
        IReadOnlyList<RagEvidence> automaticEvidence = AddEvidence(
            requestMatches
                .Concat(retrievedMatches)
                .DistinctBy(command => command.CommandPath, StringComparer.OrdinalIgnoreCase)
                .Take(1));
        AIFunction findCommands = AIFunctionFactory.Create(
            ([Description("Natural-language description of the CLI operation to find.")] string request) =>
            {
                if (string.IsNullOrWhiteSpace(request) ||
                    request.Length > MaximumToolRequestCharacters)
                {
                    return new RepositoryCliCommandSearchToolResult(
                        [],
                        $"The CLI search request must contain between 1 and {MaximumToolRequestCharacters} characters.");
                }

                IReadOnlyList<RagEvidence> evidence = AddEvidence(
                    commandCatalog.Search(request));
                return evidence.Count == 0
                    ? new RepositoryCliCommandSearchToolResult(
                        [],
                        "No matching CLI command was found. Use an exact path from the available command paths when possible.")
                    : new RepositoryCliCommandSearchToolResult(evidence, null);
            },
            name: "find_cli_commands",
            description: "Finds matching repository CLI commands from a natural-language request and returns exact live invocations.");
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

                string evidenceText = BuildEvidenceText(command);
                RagEvidence evidence = invocationContextAccessor.Current.AddEvidence(
                    evidenceText,
                    Citation,
                    similarity: 1,
                    requiredInlineCode: [command.Invocation]);
                return new RepositoryCliCommandToolResult(evidence, null);
            },
            name: "get_cli_command",
            description: "Gets the exact invocation for one repository CLI command from the live System.CommandLine catalog.");

        return ValueTask.FromResult(new AIContext
        {
            Instructions =
                "The application may supply matching live CLI commands automatically. " +
                "For a CLI question without sufficient supplied command evidence, call find_cli_commands using the user's request. " +
                "Call get_cli_command when an exact command path is already known. " +
                $"Available command paths: {string.Join(", ", commandCatalog.CommandPaths)}.",
            Messages = automaticEvidence.Count == 0
                ? []
                : [new ChatMessage(ChatRole.User, BuildEvidenceMessage(automaticEvidence))],
            Tools = [findCommands, getCommand],
        });

        IReadOnlyList<RagEvidence> AddEvidence(
            IEnumerable<RepositoryCliCommand> commands) => commands
            .Select(command => invocationContextAccessor.Current.AddEvidence(
                BuildEvidenceText(command),
                Citation,
                similarity: 1,
                requiredInlineCode: [command.Invocation]))
            .ToArray();
    }

    private static string BuildEvidenceText(RepositoryCliCommand command) =>
        string.IsNullOrWhiteSpace(command.Description)
            ? $"Command `{command.CommandPath}` uses the exact invocation `{command.Invocation}`."
            : $"Command `{command.CommandPath}` uses the exact invocation `{command.Invocation}`. {command.Description}";

    private static string BuildEvidenceMessage(IReadOnlyList<RagEvidence> evidence)
    {
        StringBuilder builder = new();
        builder.AppendLine("The application supplied the following matching live CLI commands as data:");
        builder.AppendLine("<cli_command_evidence>");
        foreach (RagEvidence item in evidence)
        {
            builder.AppendLine(
                $"citationId: {item.CitationId}\n{item.Citation}\n{item.Text}\n");
        }
        builder.AppendLine("</cli_command_evidence>");
        return builder.ToString();
    }
}

internal sealed record RepositoryCliCommandToolResult(
    RagEvidence? Evidence,
    string? Error);

internal sealed record RepositoryCliCommandSearchToolResult(
    IReadOnlyList<RagEvidence> Evidence,
    string? Message);
