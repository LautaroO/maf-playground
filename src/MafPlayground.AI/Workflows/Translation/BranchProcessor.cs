using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Workflows.Translation;

public sealed class TranslationBranchProcessor(
    ITranslationModel translationModel,
    IOptions<TranslationWorkflowOptions> options)
{
    private readonly TranslationWorkflowOptions _options = options.Value;

    public async ValueTask<TranslationCandidate> TranslateAsync(
        TranslationWorkflowRequest request,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        try
        {
            string translatedText = await ExecuteWithTimeoutAsync(
                token => translationModel.TranslateAsync(
                    request.Text,
                    targetLanguage,
                    repairIssues: null,
                    token),
                cancellationToken);
            return new TranslationCandidate(
                request.Text,
                targetLanguage,
                translatedText,
                Attempts: 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new TranslationCandidate(
                request.Text,
                targetLanguage,
                TranslatedText: null,
                Attempts: 1,
                Error: exception.Message);
        }
    }

    public async ValueTask<ValidatedTranslation> ValidateAndRepairAsync(
        TranslationCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.Error is not null || string.IsNullOrWhiteSpace(candidate.TranslatedText))
        {
            return Failed(candidate, candidate.Error ?? "The translation was empty.");
        }

        TranslationCandidate current = candidate;
        for (int repairAttempt = 0; ; repairAttempt++)
        {
            TranslationValidation validation;
            try
            {
                validation = await ValidateAsync(current, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Failed(current, $"Validation failed: {exception.Message}");
            }

            bool accepted = validation.IsValid &&
                validation.Confidence >= _options.MinimumValidationConfidence;
            IReadOnlyList<string> validationIssues = validation.Issues.Count > 0
                ? validation.Issues
                : accepted
                    ? []
                    : ["The validation confidence was below the required threshold."];
            if (accepted || repairAttempt >= _options.MaxRepairAttempts)
            {
                return new ValidatedTranslation(
                    current.TargetLanguage,
                    current.TranslatedText,
                    accepted,
                    validation.Confidence,
                    validationIssues,
                    current.Attempts);
            }

            try
            {
                string repairedText = await ExecuteWithTimeoutAsync(
                    token => translationModel.TranslateAsync(
                        current.SourceText,
                        current.TargetLanguage,
                        validationIssues,
                        token),
                    cancellationToken);
                current = current with
                {
                    TranslatedText = repairedText,
                    Attempts = current.Attempts + 1,
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Failed(current, $"Repair failed: {exception.Message}");
            }
        }
    }

    private async Task<TranslationValidation> ValidateAsync(
        TranslationCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.TranslatedText!.Length > _options.MaxInputCharacters * 5)
        {
            return new TranslationValidation(
                false,
                0,
                ["The translation exceeds the allowed output length."]);
        }

        return await ExecuteWithTimeoutAsync(
            token => translationModel.ValidateAsync(
                candidate.SourceText,
                candidate.TargetLanguage,
                candidate.TranslatedText,
                token),
            cancellationToken);
    }

    private async Task<T> ExecuteWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.ModelCallTimeout);

        try
        {
            return await operation(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The model call exceeded {_options.ModelCallTimeout}.");
        }
    }

    private static ValidatedTranslation Failed(
        TranslationCandidate candidate,
        string error) =>
        new(
            candidate.TargetLanguage,
            candidate.TranslatedText,
            IsValid: false,
            Confidence: 0,
            Issues: [error],
            Attempts: candidate.Attempts,
            Error: error);
}
