namespace MafPlayground.AI.Agents.BasicRagAgent;

public sealed class CitationValidator
{
    public const int MaximumClarifyingQuestionCharacters = 512;
    public const int MaximumConversationalResponseCharacters = 1_000;

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
        if (draft.ConversationalResponse is not null)
        {
            if (draft.InsufficientEvidence)
            {
                issues.Add("A conversational response cannot report insufficient evidence.");
            }
            if (draft.ClarifyingQuestion is not null)
            {
                issues.Add("A conversational response cannot contain a clarifying question.");
            }
            if (string.IsNullOrWhiteSpace(draft.ConversationalResponse))
            {
                issues.Add("A conversational response cannot be empty.");
            }
            else if (draft.ConversationalResponse.Length > MaximumConversationalResponseCharacters)
            {
                issues.Add(
                    $"A conversational response cannot exceed {MaximumConversationalResponseCharacters} characters.");
            }
            if (claims.Count > 0)
            {
                issues.Add("A conversational response cannot contain claims.");
            }
            if (evidence.Values.Any(item => item.RequiredInlineCode?.Count > 0))
            {
                issues.Add(
                    "A conversational response cannot ignore application-required evidence.");
            }

            return new RagAnswerValidationResult(issues.Count == 0, issues);
        }

        if (draft.ClarifyingQuestion is not null)
        {
            if (!draft.InsufficientEvidence)
            {
                issues.Add("A clarifying question requires insufficientEvidence to be true.");
            }
            if (string.IsNullOrWhiteSpace(draft.ClarifyingQuestion))
            {
                issues.Add("A clarifying question cannot be empty.");
            }
            else if (draft.ClarifyingQuestion.Length > MaximumClarifyingQuestionCharacters)
            {
                issues.Add(
                    $"A clarifying question cannot exceed {MaximumClarifyingQuestionCharacters} characters.");
            }
            if (claims.Count > 0)
            {
                issues.Add("A clarifying question cannot contain claims.");
            }
            if (evidence.Values.Any(item => item.RequiredInlineCode?.Count > 0))
            {
                issues.Add(
                    "A clarifying question cannot ignore application-required evidence.");
            }

            return new RagAnswerValidationResult(issues.Count == 0, issues);
        }

        if (draft.InsufficientEvidence)
        {
            if (claims.Count > 0)
            {
                issues.Add("An insufficient-evidence response cannot contain claims.");
            }

            if (evidence.Values.Any(item => item.RequiredInlineCode?.Count > 0))
            {
                issues.Add(
                    "An insufficient-evidence response cannot ignore application-required evidence.");
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

                foreach (string requiredLiteral in citedEvidence
                    .SelectMany(item => item.RequiredInlineCode ?? []))
                {
                    if (!claim.Text.Contains(
                            $"`{requiredLiteral}`",
                            StringComparison.Ordinal))
                    {
                        issues.Add(
                            $"Claim {index + 1} must include required inline code '{requiredLiteral}' verbatim.");
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
            return draft.ClarifyingQuestion?.Trim() ?? NoEvidenceAnswer;
        }

        if (draft.ConversationalResponse is not null)
        {
            return draft.ConversationalResponse.Trim();
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
