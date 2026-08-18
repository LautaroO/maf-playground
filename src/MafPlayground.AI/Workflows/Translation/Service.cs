using System.Diagnostics;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Budget;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Workflows.Translation;

public sealed class TranslationService(
    ITranslationModel translationModel,
    IOptions<TranslationWorkflowOptions> options,
    WorkflowGuardCoordinator guards)
{
    private readonly TranslationWorkflowOptions _options = options.Value;

    internal async ValueTask<TranslationBranchState> TranslateAsync(
        TranslationBranchState state,
        CancellationToken cancellationToken)
    {
        int attempts = state.Attempts + 1;
        try
        {
            using GuardExecutionScope guardScope = guards.EnterScope(
                state.GuardExecutionId);
            string translatedText = await translationModel.TranslateAsync(
                new TranslationDraftRequest(
                    state.SourceText,
                    state.TargetLanguage,
                    state.Attempts == 0 ? null : state.TranslatedText,
                    state.Feedback),
                cancellationToken);
            return state with
            {
                TranslatedText = translatedText,
                Attempts = attempts,
                IsValid = false,
                Confidence = 0,
                Feedback = null,
                ValidationIssues = null,
                ShouldRetry = false,
                Error = null,
                ErrorType = null,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BudgetExceededException exception)
        {
            return state with
            {
                Attempts = attempts,
                IsValid = false,
                Confidence = 0,
                Feedback = null,
                ValidationIssues =
                [
                    TranslationWorkflowHelpers.Blocking(
                        TranslationIssueCode.BudgetExceeded,
                        $"The AI execution budget for '{exception.Resource}' was exceeded."),
                ],
                ShouldRetry = false,
                Error = $"The AI execution budget for '{exception.Resource}' was exceeded.",
                ErrorType = exception.GetType().FullName,
            };
        }
        catch (Exception exception)
        {
            return state with
            {
                Attempts = attempts,
                IsValid = false,
                Confidence = 0,
                Feedback = null,
                ValidationIssues =
                [
                    TranslationWorkflowHelpers.Blocking(
                        TranslationIssueCode.ModelCallFailed,
                        "The translation model call failed."),
                ],
                ShouldRetry = false,
                Error = "The translation model call failed.",
                ErrorType = exception.GetType().FullName,
            };
        }
    }

    internal async ValueTask<TranslationBranchState> ValidateAsync(
        TranslationBranchState state,
        CancellationToken cancellationToken)
    {
        if (state.Error is not null || string.IsNullOrWhiteSpace(state.TranslatedText))
        {
            return FailedState(
                state,
                state.Error ?? "The translation was empty.",
                state.ErrorType ?? "invalid_translation");
        }

        IReadOnlyList<TranslationIssue> deterministicIssues =
            TranslationWorkflowHelpers.ValidateDeterministic(
                state.SourceText,
                state.TranslatedText,
                _options.MaxInputCharacters * 5)
            .Concat(ValidateAdditiveRepairScope(state))
            .Distinct()
            .ToArray();

        TranslationValidation validation;
        try
        {
            using GuardExecutionScope guardScope = guards.EnterScope(
                state.GuardExecutionId);
            IReadOnlyList<TrackedTranslationIssue> previousOpenIssues =
                state.OpenValidationIssues ?? [];
            validation = await translationModel.ValidateAsync(
                new TranslationValidationRequest(
                    state.SourceText,
                    state.TargetLanguage,
                    state.TranslatedText,
                    state.LastValidatedText,
                    previousOpenIssues
                        .Select(issue => new TranslationIssueReference(
                            issue.Id,
                            issue.Issue.Code,
                            issue.Issue.Description))
                        .ToArray()),
                cancellationToken);
            ValidatePreviousIssueResolutions(
                previousOpenIssues,
                validation.PreviousIssueResolutions ?? [],
                validation.Issues,
                validation.IsValid);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BudgetExceededException exception)
        {
            return FailedState(
                state,
                $"The AI execution budget for '{exception.Resource}' was exceeded.",
                exception.GetType().FullName ?? exception.GetType().Name);
        }
        catch (Exception exception)
        {
            return FailedState(
                state,
                "The translation validation call failed.",
                exception.GetType().FullName ?? exception.GetType().Name);
        }

        IReadOnlyList<TrackedTranslationIssue> previousIssues =
            state.OpenValidationIssues ?? [];
        Dictionary<string, TranslationIssueResolutionStatus> resolutions =
            (validation.PreviousIssueResolutions ?? [])
                .ToDictionary(resolution => resolution.IssueId, resolution => resolution.Status);
        List<TrackedTranslationIssue> stillOpenPreviousIssues = previousIssues
            .Where(issue =>
                resolutions[issue.Id] == TranslationIssueResolutionStatus.StillPresent ||
                deterministicIssues.Contains(issue.Issue))
            .ToList();

        List<TranslationIssue> validationIssues = [.. deterministicIssues];
        validationIssues.AddRange(stillOpenPreviousIssues.Select(issue => issue.Issue));
        IReadOnlyList<TranslationIssue> newModelIssues =
            TranslationWorkflowHelpers.NormalizeModelIssues(validation.Issues);
        validationIssues.AddRange(newModelIssues);
        validationIssues = validationIssues.Distinct().ToList();

        if (!validation.IsValid &&
            !validationIssues.Any(issue =>
                issue.Severity == TranslationIssueSeverity.Blocking))
        {
            validationIssues.Add(
                TranslationWorkflowHelpers.Warning(
                    TranslationIssueCode.ValidatorUncertain,
                    "The validator was not confident enough to confirm the translation."));
        }

        if (validation.Confidence < _options.MinimumValidationConfidence &&
            !validationIssues.Any(issue =>
                issue.Severity == TranslationIssueSeverity.Blocking) &&
            !validationIssues.Any(issue => issue.Code == TranslationIssueCode.LowConfidence))
        {
            validationIssues.Add(
                TranslationWorkflowHelpers.Warning(
                    TranslationIssueCode.LowConfidence,
                    "The validator confidence was below the configured threshold."));
        }

        bool hasBlockingIssues = validationIssues.Any(issue =>
            issue.Severity == TranslationIssueSeverity.Blocking);
        bool accepted = !hasBlockingIssues;
        IReadOnlyList<string> retryFeedback = validationIssues
            .Where(issue => issue.Severity == TranslationIssueSeverity.Blocking)
            .Select(issue => $"{issue.Code}: {issue.Description}")
            .ToArray();
        bool shouldRetry = hasBlockingIssues &&
            state.Attempts <= _options.MaxTranslationRetries;
        IReadOnlyList<TrackedTranslationIssue> openIssues = TrackOpenIssues(
            validationIssues,
            stillOpenPreviousIssues,
            state.Attempts);

        Activity.Current?.SetTag(
            "maf_playground.translation.previous_blocking_issue_count",
            previousIssues.Count);
        Activity.Current?.SetTag(
            "maf_playground.translation.resolved_issue_count",
            previousIssues.Count - stillOpenPreviousIssues.Count);
        Activity.Current?.SetTag(
            "maf_playground.translation.still_present_issue_count",
            stillOpenPreviousIssues.Count);
        Activity.Current?.SetTag(
            "maf_playground.translation.new_issue_count",
            newModelIssues.Count);
        Activity.Current?.SetTag(
            "maf_playground.translation.open_blocking_issue_count",
            openIssues.Count);

        return state with
        {
            IsValid = accepted,
            Confidence = validation.Confidence,
            Feedback = retryFeedback,
            ValidationIssues = validationIssues,
            ShouldRetry = shouldRetry,
            ErrorType = accepted ? null : "validation_rejected",
            OpenValidationIssues = openIssues,
            LastValidatedText = state.TranslatedText,
        };
    }

    internal static ValidatedTranslation Complete(TranslationBranchState state) =>
        new(
            state.TargetLanguage,
            state.TranslatedText,
            state.IsValid,
            state.Confidence,
            state.ValidationIssues ?? [],
            state.Attempts,
            state.Error);

    private static TranslationBranchState FailedState(
        TranslationBranchState state,
        string error,
        string errorType) =>
        state with
        {
            IsValid = false,
            Confidence = 0,
            Feedback = [error],
            ValidationIssues =
            [
                TranslationWorkflowHelpers.Blocking(
                    MapErrorCode(errorType),
                    error),
            ],
            ShouldRetry = false,
            Error = error,
            ErrorType = errorType,
        };

    private static TranslationIssueCode MapErrorCode(string errorType) =>
        errorType switch
        {
            "invalid_translation" => TranslationIssueCode.EmptyTranslation,
            _ when errorType.Contains("Budget", StringComparison.OrdinalIgnoreCase) =>
                TranslationIssueCode.BudgetExceeded,
            _ when errorType.Contains("Validation", StringComparison.OrdinalIgnoreCase) =>
                TranslationIssueCode.ValidationCallFailed,
            _ => TranslationIssueCode.ModelCallFailed,
        };

    private static void ValidatePreviousIssueResolutions(
        IReadOnlyList<TrackedTranslationIssue> previousIssues,
        IReadOnlyList<TranslationIssueResolution> resolutions,
        IReadOnlyList<TranslationIssue> newIssues,
        bool isValid)
    {
        string[] expectedIds = previousIssues
            .Select(issue => issue.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actualIds = resolutions
            .Select(resolution => resolution.IssueId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
        {
            throw new TranslationValidationConsistencyException(
                "The translation validator did not account for every previous blocking issue.");
        }

        if (previousIssues.Count == 0)
        {
            return;
        }

        if (newIssues.Count > 0)
        {
            throw new TranslationValidationConsistencyException(
                "Repair verification returned findings outside the previous issue scope.");
        }

        bool hasStillPresentIssue = resolutions.Any(resolution =>
            resolution.Status == TranslationIssueResolutionStatus.StillPresent);
        if (isValid == hasStillPresentIssue)
        {
            throw new TranslationValidationConsistencyException(
                "Repair verification validity contradicts its issue resolutions.");
        }
    }

    private static IReadOnlyList<TrackedTranslationIssue> TrackOpenIssues(
        IReadOnlyList<TranslationIssue> currentIssues,
        IReadOnlyList<TrackedTranslationIssue> stillOpenPreviousIssues,
        int attempt)
    {
        List<TrackedTranslationIssue> tracked = [.. stillOpenPreviousIssues];
        int nextIssueIndex = 0;
        foreach (TranslationIssue issue in currentIssues.Where(issue =>
                     issue.Severity == TranslationIssueSeverity.Blocking))
        {
            if (tracked.Any(existing => existing.Issue == issue))
            {
                continue;
            }

            tracked.Add(new TrackedTranslationIssue(
                $"attempt-{attempt}-issue-{nextIssueIndex++}-{issue.Code}",
                issue));
        }

        return tracked;
    }

    private static IReadOnlyList<TranslationIssue> ValidateAdditiveRepairScope(
        TranslationBranchState state)
    {
        IReadOnlyList<TrackedTranslationIssue> previousIssues =
            state.OpenValidationIssues ?? [];
        if (previousIssues.Count == 0 ||
            string.IsNullOrEmpty(state.LastValidatedText) ||
            string.IsNullOrEmpty(state.TranslatedText) ||
            previousIssues.Any(issue => issue.Issue.Code is not
                (TranslationIssueCode.MissingData or TranslationIssueCode.PlaceholderChanged)))
        {
            return [];
        }

        int maximumAddedCharacters = previousIssues.Count * 32;
        bool preservedPreviousDraft = IsSubsequence(
            state.LastValidatedText,
            state.TranslatedText);
        bool boundedGrowth =
            state.TranslatedText.Length - state.LastValidatedText.Length <=
            maximumAddedCharacters;
        return preservedPreviousDraft && boundedGrowth
            ? []
            : [TranslationWorkflowHelpers.Blocking(
                TranslationIssueCode.OutputFormat,
                "The repair changed content outside its additive issue scope.")];
    }

    private static bool IsSubsequence(string expected, string actual)
    {
        int expectedIndex = 0;
        foreach (char character in actual)
        {
            if (expectedIndex < expected.Length &&
                character == expected[expectedIndex])
            {
                expectedIndex++;
            }
        }

        return expectedIndex == expected.Length;
    }
}

internal sealed class TranslationValidationConsistencyException(string message)
    : InvalidOperationException(message);
