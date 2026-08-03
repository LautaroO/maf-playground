namespace MafPlayground.AI.Workflows.Translation;

public interface ITranslationModel
{
    Task<string> TranslateAsync(
        string sourceText,
        string targetLanguage,
        IReadOnlyList<string>? repairIssues,
        CancellationToken cancellationToken);

    Task<TranslationValidation> ValidateAsync(
        string sourceText,
        string targetLanguage,
        string translatedText,
        CancellationToken cancellationToken);
}
