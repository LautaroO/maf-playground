using System.CommandLine;
using System.Globalization;
using System.Text;
using MafPlayground.AI.Agents.RepositoryHelpAgent;

namespace MafPlayground.CLI.Documentation;

public sealed class SystemCommandLineRepositoryCliCommandCatalog
    : IRepositoryCliCommandCatalog
{
    private readonly IReadOnlyDictionary<string, RepositoryCliCommand> _commands;

    public SystemCommandLineRepositoryCliCommandCatalog(RootCommand rootCommand)
    {
        ArgumentNullException.ThrowIfNull(rootCommand);
        Dictionary<string, RepositoryCliCommand> commands =
            new(StringComparer.OrdinalIgnoreCase);
        AddCommands(commands, rootCommand, []);
        _commands = commands;
        CommandPaths = commands.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> CommandPaths { get; }

    public RepositoryCliCommand? Find(string commandPath)
    {
        if (string.IsNullOrWhiteSpace(commandPath))
        {
            return null;
        }

        string normalized = string.Join(
            ' ',
            commandPath.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return _commands.GetValueOrDefault(normalized);
    }

    public IReadOnlyList<RepositoryCliCommand> Search(
        string request,
        int maxResults = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        if (maxResults is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                maxResults,
                "CLI command search maxResults must be between 1 and 10.");
        }

        string normalizedRequest = NormalizeText(request);
        string[] requestTokens = Tokenize(normalizedRequest);
        if (requestTokens.Length == 0)
        {
            return [];
        }

        return _commands.Values
            .Select(command => new
            {
                Command = command,
                Score = Score(command, normalizedRequest, requestTokens),
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Command.CommandPath, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(candidate => candidate.Command)
            .ToArray();
    }

    private static int Score(
        RepositoryCliCommand command,
        string normalizedRequest,
        IReadOnlyList<string> requestTokens)
    {
        string normalizedPath = NormalizeText(command.CommandPath);
        string[] pathTokens = Tokenize(normalizedPath);
        string leaf = command.CommandPath.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
        string[] leafTokens = Tokenize(NormalizeText(leaf));
        bool exactPath = ContainsPhrase(normalizedRequest, normalizedPath);
        bool exactRequest = string.Equals(
            normalizedRequest,
            normalizedPath,
            StringComparison.Ordinal);
        bool leafMatched = leafTokens.Length > 0 && leafTokens.All(
            token => requestTokens.Any(requestToken => TokensMatch(token, requestToken)));
        if (!exactPath && !leafMatched)
        {
            return 0;
        }

        int pathMatches = pathTokens.Count(
            token => requestTokens.Any(requestToken => TokensMatch(token, requestToken)));
        string[] descriptionTokens = Tokenize(NormalizeText(command.Description));
        int descriptionMatches = descriptionTokens
            .Distinct(StringComparer.Ordinal)
            .Count(token => requestTokens.Contains(token, StringComparer.Ordinal));
        return (exactPath && (pathTokens.Length > 1 || exactRequest) ? 100 : 0) +
            (leafMatched ? 50 : 0) +
            (pathMatches * 10) +
            Math.Min(descriptionMatches, 10);
    }

    private static bool ContainsPhrase(string text, string phrase) =>
        $" {text} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private static bool TokensMatch(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal) ||
        left.Length >= 4 && right.Length >= 4 &&
        Math.Abs(left.Length - right.Length) <= 1 &&
        (left.StartsWith(right, StringComparison.Ordinal) ||
            right.StartsWith(left, StringComparison.Ordinal));

    private static string[] Tokenize(string normalized) => normalized.Split(
        ' ',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeText(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        bool previousWasSeparator = true;
        foreach (char character in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append(' ');
                previousWasSeparator = true;
            }
        }
        return builder.ToString().Trim();
    }

    private static void AddCommands(
        IDictionary<string, RepositoryCliCommand> entries,
        Command parent,
        IReadOnlyList<string> parentPath)
    {
        foreach (Command command in parent.Subcommands.Where(command => !command.Hidden))
        {
            string[] path = [.. parentPath, command.Name];
            string commandPath = string.Join(' ', path);
            entries.Add(
                commandPath,
                new RepositoryCliCommand(
                    commandPath,
                    RepositoryHelpCliReferenceGenerator.BuildInvocation(command, path),
                    command.Description ?? string.Empty));
            AddCommands(entries, command, path);
        }
    }
}
