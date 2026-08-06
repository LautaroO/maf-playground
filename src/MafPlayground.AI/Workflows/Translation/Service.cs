using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Workflows.Translation;

public sealed class TranslationService(
    ITranslationModel translationModel,
    IOptions<TranslationWorkflowOptions> options)
{
    private readonly TranslationWorkflowOptions _options = options.Value;

    internal async ValueTask<TranslationBranchState> TranslateAsync(
        TranslationBranchState state,
        CancellationToken cancellationToken)
    {
        int attempts = state.Attempts + 1;
        try
        {
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
                ShouldRetry = false,
                Error = null,
                ErrorType = null,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return state with
            {
                Attempts = attempts,
                IsValid = false,
                Confidence = 0,
                Feedback = null,
                ShouldRetry = false,
                Error = exception.Message,
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

        TranslationValidation validation;
        try
        {
            if (state.TranslatedText.Length > _options.MaxInputCharacters * 5)
            {
                validation = new TranslationValidation(
                    false,
                    0,
                    ["The translation exceeds the allowed output length."]);
            }
            else
            {
                validation = await translationModel.ValidateAsync(
                    state.SourceText,
                    state.TargetLanguage,
                    state.TranslatedText,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return FailedState(
                state,
                $"Validation failed: {exception.Message}",
                exception.GetType().FullName ?? exception.GetType().Name);
        }

        bool accepted = validation.IsValid &&
            validation.Confidence >= _options.MinimumValidationConfidence;
        IReadOnlyList<string> validationIssues = validation.Issues.Count > 0
            ? validation.Issues
            : accepted
                ? []
                : ["The validation confidence was below the required threshold."];
        bool shouldRetry = !accepted && state.Attempts <= _options.MaxTranslationRetries;
        return state with
        {
            IsValid = accepted,
            Confidence = validation.Confidence,
            Feedback = validationIssues,
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
            state.Feedback ?? [],
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
            ShouldRetry = false,
            Error = error,
            ErrorType = errorType,
        };
}
