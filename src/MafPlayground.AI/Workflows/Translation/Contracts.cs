namespace MafPlayground.AI.Workflows.Translation;

public sealed record TranslationWorkflowInput(
    string Text,
    IReadOnlyList<string> TargetLanguages);

public sealed record TranslationWorkflowRequest(
    string Text,
    IReadOnlyList<string> TargetLanguages);

internal sealed record TranslationBranchState(
    string SourceText,
    IReadOnlyList<string> RequestedTargetLanguages,
    string TargetLanguage,
    string? TranslatedText = null,
    int Attempts = 0,
    bool IsValid = false,
    double Confidence = 0,
    IReadOnlyList<string>? Feedback = null,
    bool ShouldRetry = false,
    string? Error = null,
    string? ErrorType = null);

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
    IReadOnlyList<string> RequestedTargetLanguages,
    ValidatedTranslation Translation);
