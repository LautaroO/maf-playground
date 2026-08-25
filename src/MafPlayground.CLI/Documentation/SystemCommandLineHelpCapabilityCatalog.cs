using System.CommandLine;
using System.Globalization;
using System.Text;
using MafPlayground.AI.Agents.HelpAgent;

namespace MafPlayground.CLI.Documentation;

public sealed class SystemCommandLineHelpCapabilityCatalog
    : IHelpCapabilityCatalog
{
    private const string Citation =
        "[MafPlayground CLI reference, source: cli-reference.md]";
    private readonly IReadOnlyDictionary<string, HelpCapability> _capabilities;

    public SystemCommandLineHelpCapabilityCatalog(RootCommand rootCommand)
    {
        ArgumentNullException.ThrowIfNull(rootCommand);
        Dictionary<string, HelpCapability> capabilities =
            new(StringComparer.OrdinalIgnoreCase);
        AddCommands(capabilities, rootCommand, []);
        _capabilities = capabilities;
        CapabilityIds = capabilities.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> CapabilityIds { get; }

    public HelpCapability? Find(string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId))
        {
            return null;
        }

        string normalized = string.Join(
            ' ',
            capabilityId.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return _capabilities.GetValueOrDefault(normalized);
    }

    public IReadOnlyList<HelpCapability> Search(
        string request,
        int maxResults = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        if (maxResults is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                maxResults,
                "Help capability search maxResults must be between 1 and 10.");
        }

        string normalizedRequest = NormalizeText(request);
        string[] requestTokens = Tokenize(normalizedRequest);
        if (requestTokens.Length == 0)
        {
            return [];
        }

        return _capabilities.Values
            .Select(capability => new
            {
                Capability = capability,
                Score = Score(capability, normalizedRequest, requestTokens),
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Capability.Id, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(candidate => candidate.Capability)
            .ToArray();
    }

    private static int Score(
        HelpCapability capability,
        string normalizedRequest,
        IReadOnlyList<string> requestTokens)
    {
        string normalizedId = NormalizeText(capability.Id);
        string[] idTokens = Tokenize(normalizedId);
        string leaf = capability.Id.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
        string[] leafTokens = Tokenize(NormalizeText(leaf));
        bool exactId = ContainsPhrase(normalizedRequest, normalizedId);
        bool exactRequest = string.Equals(
            normalizedRequest,
            normalizedId,
            StringComparison.Ordinal);
        bool leafMatched = leafTokens.Length > 0 && leafTokens.All(
            token => requestTokens.Any(requestToken => TokensMatch(token, requestToken)));
        if (!exactId && !leafMatched)
        {
            return 0;
        }

        int idMatches = idTokens.Count(
            token => requestTokens.Any(requestToken => TokensMatch(token, requestToken)));
        string[] evidenceTokens = Tokenize(NormalizeText(capability.EvidenceText));
        int evidenceMatches = evidenceTokens
            .Distinct(StringComparer.Ordinal)
            .Count(token => requestTokens.Contains(token, StringComparer.Ordinal));
        return (exactId && (idTokens.Length > 1 || exactRequest) ? 100 : 0) +
            (leafMatched ? 50 : 0) +
            (idMatches * 10) +
            Math.Min(evidenceMatches, 10);
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
        IDictionary<string, HelpCapability> entries,
        Command parent,
        IReadOnlyList<string> parentPath)
    {
        foreach (Command command in parent.Subcommands.Where(command => !command.Hidden))
        {
            string[] path = [.. parentPath, command.Name];
            string commandPath = string.Join(' ', path);
            string invocation = RepositoryHelpCliReferenceGenerator.BuildInvocation(command, path);
            string evidenceText = string.IsNullOrWhiteSpace(command.Description)
                ? $"Command `{commandPath}` uses the exact invocation `{invocation}`."
                : $"Command `{commandPath}` uses the exact invocation `{invocation}`. {command.Description}";
            entries.Add(
                commandPath,
                new HelpCapability(
                    commandPath,
                    evidenceText,
                    Citation,
                    [invocation]));
            AddCommands(entries, command, path);
        }
    }
}
