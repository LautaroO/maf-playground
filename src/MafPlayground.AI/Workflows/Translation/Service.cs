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
                state.SourceText,
                state.TargetLanguage,
                state.Feedback,
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
                _options.MaxInputCharacters * 5);

        TranslationValidation validation;
        try
        {
            using GuardExecutionScope guardScope = guards.EnterScope(
                state.GuardExecutionId);
            validation = await translationModel.ValidateAsync(
                state.SourceText,
                state.TargetLanguage,
                state.TranslatedText,
                cancellationToken);
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

        List<TranslationIssue> validationIssues = [.. deterministicIssues];
        validationIssues.AddRange(
            TranslationWorkflowHelpers.NormalizeModelIssues(validation.Issues));

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

        return state with
        {
            IsValid = accepted,
            Confidence = validation.Confidence,
            Feedback = retryFeedback,
            ValidationIssues = validationIssues,
            ShouldRetry = shouldRetry,
            ErrorType = accepted ? null : "validation_rejected",
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
}
