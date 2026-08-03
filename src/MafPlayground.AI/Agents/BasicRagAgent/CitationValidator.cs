using System.Text.RegularExpressions;

namespace MafPlayground.AI.Agents.BasicRagAgent;

public sealed partial class CitationValidator
{
    public const string NoEvidenceAnswer = "The knowledge base does not contain enough information to answer that question.";

    public bool IsValid(string response, IReadOnlySet<string> allowedCitations)
    {
        if (allowedCitations.Count == 0) return response.Contains(NoEvidenceAnswer, StringComparison.Ordinal);
        MatchCollection citations = CitationRegex().Matches(response);
        return citations.Count > 0 && citations.All(match => allowedCitations.Contains(match.Value));
    }

    [GeneratedRegex(@"\[[^\]\r\n]+, (?:page \d+, )?source: [^\]\r\n]+\]", RegexOptions.CultureInvariant)]
    private static partial Regex CitationRegex();
}
