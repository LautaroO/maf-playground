namespace MafPlayground.AI.Agents.BasicRagAgent;

public sealed class CitationValidator
{
    public const string NoEvidenceAnswer =
        "The knowledge base does not contain enough information to answer that question.";

    public RagAnswerValidationResult Validate(
        RagAnswerDraft? draft,
        IReadOnlyDictionary<string, RagEvidence> evidence)
    {
        List<string> issues = [];
        if (draft is null)
        {
            return new RagAnswerValidationResult(false, ["The response was empty."]);
        }

        IReadOnlyList<RagClaim> claims = draft.Claims ?? [];
        if (draft.InsufficientEvidence)
        {
            if (claims.Count > 0)
            {
                issues.Add("An insufficient-evidence response cannot contain claims.");
            }

            return new RagAnswerValidationResult(issues.Count == 0, issues);
        }

        if (evidence.Count == 0)
        {
            issues.Add("Claims cannot be returned when no evidence was retrieved.");
        }

        if (claims.Count == 0)
        {
            issues.Add("A supported answer requires at least one claim.");
        }

        for (int index = 0; index < claims.Count; index++)
        {
            RagClaim claim = claims[index];
            if (string.IsNullOrWhiteSpace(claim.Text))
            {
                issues.Add($"Claim {index + 1} has no text.");
            }

            IReadOnlyList<string> citationIds = claim.CitationIds ?? [];
            if (citationIds.Count == 0)
            {
                issues.Add($"Claim {index + 1} has no citation IDs.");
                continue;
            }

            foreach (string citationId in citationIds)
            {
                if (string.IsNullOrWhiteSpace(citationId) ||
                    !evidence.ContainsKey(citationId))
                {
                    issues.Add($"Claim {index + 1} references an unknown citation ID.");
                }
            }

            if (!string.IsNullOrWhiteSpace(claim.Text))
            {
                IReadOnlyList<RagEvidence> citedEvidence = citationIds
                    .Where(evidence.ContainsKey)
                    .Distinct(StringComparer.Ordinal)
                    .Select(citationId => evidence[citationId])
                    .ToArray();
                foreach (string literal in ExtractInlineCode(claim.Text))
                {
                    if (!citedEvidence.Any(item =>
                            item.Text.Contains(literal, StringComparison.Ordinal)))
                    {
                        issues.Add(
                            $"Claim {index + 1} inline code '{literal}' does not appear verbatim in its cited evidence.");
                    }
                }
            }
        }

        return new RagAnswerValidationResult(issues.Count == 0, issues);
    }

    public string Render(
        RagAnswerDraft draft,
        IReadOnlyDictionary<string, RagEvidence> evidence)
    {
        RagAnswerValidationResult validation = Validate(draft, evidence);
        if (!validation.IsValid)
        {
            throw new ArgumentException(
                "Cannot render an invalid grounded answer.",
                nameof(draft));
        }

        if (draft.InsufficientEvidence)
        {
            return NoEvidenceAnswer;
        }

        return string.Join(
            Environment.NewLine,
            draft.Claims!.Select(claim =>
            {
                string citations = string.Join(
                    " ",
                    claim.CitationIds!
                        .Distinct(StringComparer.Ordinal)
                        .Select(id => evidence[id].Citation));
                return $"{claim.Text!.Trim()} {citations}";
            }));
    }

    private static IEnumerable<string> ExtractInlineCode(string text)
    {
        string[] segments = text.Split('`');
        for (int index = 1; index < segments.Length; index += 2)
        {
            string candidate = segments[index].Trim();
            if (candidate.Length > 0 &&
                !candidate.Contains('\r', StringComparison.Ordinal) &&
                !candidate.Contains('\n', StringComparison.Ordinal))
            {
                yield return candidate;
            }
        }
    }
}
