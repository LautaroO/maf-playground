using System.ComponentModel;

namespace MafPlayground.AI.Workflows.Translation;

[Description("Input for translating one source text into one or more target languages.")]
public sealed record TranslationWorkflowInput(
    [property: Description("The source text to translate.")]
    string Text,
    [property: Description(
        "IETF language identifiers for the requested translations, for example es or pt-BR.")]
    IReadOnlyList<string> TargetLanguages);

[Description("A translation request containing source text and requested target languages.")]
public sealed record TranslationWorkflowRequest(
    [property: Description("The source text to translate.")]
    string Text,
    [property: Description(
        "IETF language identifiers for the requested translations, for example es or pt-BR.")]
    IReadOnlyList<string> TargetLanguages);

internal sealed record GuardedTranslationRequest(
    TranslationWorkflowRequest Request,
    string GuardExecutionId);

internal sealed record TranslationBranchState(
    string SourceText,
    IReadOnlyList<string> RequestedTargetLanguages,
    string TargetLanguage,
    string GuardExecutionId,
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
    ValidatedTranslation Translation,
    string GuardExecutionId);
