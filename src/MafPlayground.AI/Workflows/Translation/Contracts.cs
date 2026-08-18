using System.ComponentModel;
using System.Text.Json.Serialization;

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

internal sealed record TrackedTranslationIssue(
    string Id,
    TranslationIssue Issue);

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
    IReadOnlyList<TranslationIssue>? ValidationIssues = null,
    bool ShouldRetry = false,
    string? Error = null,
    string? ErrorType = null,
    IReadOnlyList<TrackedTranslationIssue>? OpenValidationIssues = null,
    string? LastValidatedText = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TranslationQualityStatus
{
    Accepted,
    AcceptedWithWarnings,
    Rejected,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TranslationIssueSeverity
{
    Warning,
    Blocking,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TranslationIssueCode
{
    Unknown,
    UntranslatedContent,
    SemanticMeaningChanged,
    MissingContent,
    MissingData,
    PlaceholderChanged,
    WrongTargetLanguage,
    EmptyTranslation,
    OutputTooLong,
    OutputFormat,
    ModelCallFailed,
    ValidationCallFailed,
    BudgetExceeded,
    ValidatorUncertain,
    LowConfidence,
    ToneDifference,
    StylePreference,
    Naturalness,
    Punctuation,
    RegionalPreference,
}

public sealed record TranslationIssue(
    TranslationIssueSeverity Severity,
    TranslationIssueCode Code,
    string Description);

public sealed record TranslationValidation(
    bool IsValid,
    double Confidence,
    IReadOnlyList<TranslationIssue> Issues,
    IReadOnlyList<TranslationIssueResolution>? PreviousIssueResolutions = null);

public sealed record TranslationDraftRequest(
    string SourceText,
    string TargetLanguage,
    string? PreviousTranslatedText,
    IReadOnlyList<string>? ValidationFeedback);

public sealed record TranslationValidationRequest(
    string SourceText,
    string TargetLanguage,
    string TranslatedText,
    string? PreviousTranslatedText,
    IReadOnlyList<TranslationIssueReference> PreviousBlockingIssues);

public sealed record TranslationIssueReference(
    string Id,
    TranslationIssueCode Code,
    string Description);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TranslationIssueResolutionStatus
{
    Resolved,
    StillPresent,
}

public sealed record TranslationIssueResolution(
    string IssueId,
    TranslationIssueResolutionStatus Status);

public sealed record ValidatedTranslation(
    string TargetLanguage,
    string? TranslatedText,
    bool IsValid,
    double Confidence,
    IReadOnlyList<TranslationIssue> Issues,
    int Attempts,
    string? Error = null)
{
    public TranslationQualityStatus Status => !IsValid
        ? TranslationQualityStatus.Rejected
        : Issues.Any(issue => issue.Severity == TranslationIssueSeverity.Warning)
            ? TranslationQualityStatus.AcceptedWithWarnings
            : TranslationQualityStatus.Accepted;
}

public sealed record TranslationWorkflowResult(
    string SourceText,
    IReadOnlyList<ValidatedTranslation> Translations);

internal sealed record ValidatedTranslationMessage(
    string SourceText,
    IReadOnlyList<string> RequestedTargetLanguages,
    ValidatedTranslation Translation,
    string GuardExecutionId);
