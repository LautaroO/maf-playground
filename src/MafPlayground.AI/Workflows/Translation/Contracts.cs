namespace MafPlayground.AI.Workflows.Translation;

public sealed record TranslationWorkflowInput(string Text);

public sealed record TranslationWorkflowRequest(
    string Text,
    IReadOnlyList<string> TargetLanguages);

public sealed record TranslationCandidate(
    string SourceText,
    string TargetLanguage,
    string? TranslatedText,
    int Attempts,
    string? Error = null);

public sealed record TranslationValidation(
    bool IsValid,
    double Confidence,
    IReadOnlyList<string> Issues);

public sealed record ValidatedTranslation(
    string TargetLanguage,
    string? TranslatedText,
    bool IsValid,
    double Confidence,
    IReadOnlyList<string> Issues,
    int Attempts,
    string? Error = null);

public sealed record TranslationWorkflowResult(
    string SourceText,
    IReadOnlyList<ValidatedTranslation> Translations);

internal sealed record ValidatedTranslationMessage(
    string SourceText,
    ValidatedTranslation Translation);
