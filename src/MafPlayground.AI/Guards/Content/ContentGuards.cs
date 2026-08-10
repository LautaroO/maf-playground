using System.Text.RegularExpressions;
using MafPlayground.AI.Observability;

namespace MafPlayground.AI.Guards.Content;

public enum ContentOrigin
{
    UserInput,
    AgentOutput,
    ToolArgument,
    ToolResult,
    RetrievedContent,
}

public sealed record ContentFinding(string Category, int Start, int Length);

public sealed record ContentInspectionResult(IReadOnlyList<ContentFinding> Findings)
{
    public bool ContainsSensitiveData => Findings.Count > 0;
}

public interface IContentInspector
{
    ValueTask<ContentInspectionResult> InspectAsync(
        string content,
        ContentOrigin origin,
        CancellationToken cancellationToken = default);
}

public sealed partial class RegexPiiContentInspector : IContentInspector
{
    public ValueTask<ContentInspectionResult> InspectAsync(
        string content,
        ContentOrigin origin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        List<ContentFinding> findings = [];
        AddFindings(findings, EmailRegex().Matches(content), "EMAIL");
        AddFindings(findings, PhoneRegex().Matches(content), "PHONE");
        AddFindings(findings, CreditCardRegex().Matches(content), "PAYMENT_CARD");
        return ValueTask.FromResult(new ContentInspectionResult(
            findings.OrderBy(finding => finding.Start).ToArray()));
    }

    private static void AddFindings(
        ICollection<ContentFinding> findings,
        MatchCollection matches,
        string category)
    {
        foreach (Match match in matches)
        {
            findings.Add(new ContentFinding(category, match.Index, match.Length));
        }
    }

    [GeneratedRegex(
        @"(?<![\w.+-])[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}(?![\w-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(
        @"(?<!\d)(?:\+?\d{1,3}[ .-]?)?(?:\(?\d{2,4}\)?[ .-]?)?\d{3,4}[ .-]\d{4}(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(
        @"(?<!\d)(?:\d[ -]?){13,19}(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CreditCardRegex();
}

public sealed class ContentGuard(IContentInspector inspector)
{
    public async ValueTask<string> ApplyAsync(
        string content,
        GuardAction action,
        ContentOrigin origin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (action == GuardAction.Allow || content.Length == 0)
        {
            return content;
        }

        ContentInspectionResult inspection = await inspector
            .InspectAsync(content, origin, cancellationToken)
            .ConfigureAwait(false);
        if (!inspection.ContainsSensitiveData)
        {
            return content;
        }

        GuardTelemetry.RecordContentDecision(origin, action, inspection.Findings);
        if (action == GuardAction.Block)
        {
            throw new ContentGuardRejectedException(
                origin,
                inspection.Findings.Select(finding => finding.Category).Distinct().ToArray());
        }

        return Redact(content, inspection.Findings);
    }

    private static string Redact(string content, IReadOnlyList<ContentFinding> findings)
    {
        Dictionary<string, int> categoryCounts = new(StringComparer.Ordinal);
        List<ContentFinding> nonOverlapping = [];
        int nextAvailableIndex = 0;
        foreach (ContentFinding finding in findings.OrderBy(finding => finding.Start))
        {
            if (finding.Start < nextAvailableIndex)
            {
                continue;
            }

            nonOverlapping.Add(finding);
            nextAvailableIndex = finding.Start + finding.Length;
        }

        System.Text.StringBuilder builder = new(content.Length);
        int position = 0;
        foreach (ContentFinding finding in nonOverlapping)
        {
            builder.Append(content, position, finding.Start - position);
            int ordinal = categoryCounts.TryGetValue(finding.Category, out int count)
                ? count + 1
                : 1;
            categoryCounts[finding.Category] = ordinal;
            builder.Append('<').Append(finding.Category).Append('_').Append(ordinal).Append('>');
            position = finding.Start + finding.Length;
        }

        builder.Append(content, position, content.Length - position);
        return builder.ToString();
    }
}

public sealed class ContentGuardRejectedException(
    ContentOrigin origin,
    IReadOnlyList<string> categories)
    : InvalidOperationException(
        $"Content was rejected by the {origin} policy because it contains protected data categories: " +
        $"{string.Join(", ", categories)}.")
{
    public ContentOrigin Origin { get; } = origin;

    public IReadOnlyList<string> Categories { get; } = categories;
}
