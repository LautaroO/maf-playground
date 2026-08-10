using System.Text.RegularExpressions;
using MafPlayground.AI.Observability;

namespace MafPlayground.AI.Workflows.Translation;

internal static partial class TranslationWorkflowHelpers
{
    private static readonly TranslationIssueCode[] SubjectiveIssueCodes =
    [
        TranslationIssueCode.ToneDifference,
        TranslationIssueCode.StylePreference,
        TranslationIssueCode.Naturalness,
        TranslationIssueCode.Punctuation,
        TranslationIssueCode.RegionalPreference,
    ];

    public static TranslationWorkflowRequest ValidateRequest(
        TranslationWorkflowRequest request,
        TranslationWorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        string text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new ArgumentException("Translation text is required.", nameof(request));
        }

        if (text.Length > options.MaxInputCharacters)
        {
            throw new ArgumentException(
                $"Translation text cannot exceed {options.MaxInputCharacters} characters.",
                nameof(request));
        }

        string[] languages = ValidateLanguages(request.TargetLanguages, options);
        return new TranslationWorkflowRequest(text, languages);
    }

    public static string[] ValidateLanguages(
        IReadOnlyList<string>? targetLanguages,
        TranslationWorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string[] languages = ValidateSupportedLanguages(targetLanguages);

        if (languages.Length > options.MaxTargetLanguages)
        {
            throw new ArgumentException(
                $"A maximum of {options.MaxTargetLanguages} target languages is allowed.",
                nameof(targetLanguages));
        }

        string[] configuredLanguages = ValidateSupportedLanguages(
            options.SupportedTargetLanguages);
        HashSet<string> supportedLanguages = new(
            configuredLanguages,
            StringComparer.OrdinalIgnoreCase);
        string? unsupportedLanguage = languages.FirstOrDefault(
            language => !supportedLanguages.Contains(language));
        if (unsupportedLanguage is not null)
        {
            throw new ArgumentException(
                $"Target language '{unsupportedLanguage}' is not supported. " +
                $"Supported languages: {string.Join(", ", configuredLanguages)}.",
                nameof(targetLanguages));
        }

        return languages;
    }

    public static string[] ValidateSupportedLanguages(
        IReadOnlyList<string>? supportedLanguages)
    {
        string[] languages = supportedLanguages?
            .Select(language => language?.Trim() ?? string.Empty)
            .Where(language => language.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (languages.Length == 0)
        {
            throw new ArgumentException(
                "At least one target language is required.",
                nameof(supportedLanguages));
        }

        string? invalidLanguage = languages.FirstOrDefault(
            language => !LanguageIdentifierRegex().IsMatch(language));
        if (invalidLanguage is not null)
        {
            throw new ArgumentException(
                $"'{invalidLanguage}' is not a valid language identifier.",
                nameof(supportedLanguages));
        }

        return languages;
    }

    public static IEnumerable<int> SelectTargetIndexes(
        TranslationWorkflowRequest request,
        IReadOnlyList<string> supportedLanguages)
    {
        HashSet<string> requestedLanguages = new(
            request.TargetLanguages,
            StringComparer.OrdinalIgnoreCase);
        return supportedLanguages
            .Select((language, index) => (language, index))
            .Where(item => requestedLanguages.Contains(item.language))
            .Select(item => item.index);
    }

    public static string NormalizeExecutorId(string language) =>
        language.ToLowerInvariant().Replace('-', '_');

    public static IReadOnlyList<TranslationIssue> ValidateDeterministic(
        string sourceText,
        string translatedText,
        int maxOutputCharacters)
    {
        List<TranslationIssue> issues = [];
        string source = sourceText.Trim();
        string translation = translatedText.Trim();

        if (translation.Length == 0)
        {
            issues.Add(Blocking(
                TranslationIssueCode.EmptyTranslation,
                "The translation is empty."));
            return issues;
        }

        if (translation.Length > maxOutputCharacters)
        {
            issues.Add(Blocking(
                TranslationIssueCode.OutputTooLong,
                "The translation exceeds the allowed output length."));
        }

        if (string.Equals(source, translation, StringComparison.OrdinalIgnoreCase) &&
            source.Any(char.IsLetter))
        {
            issues.Add(Blocking(
                TranslationIssueCode.UntranslatedContent,
                "The output is identical to the source and may not have been translated."));
        }

        foreach (string token in ExtractProtectedTokens(source))
        {
            if (!translation.Contains(token, StringComparison.Ordinal))
            {
                issues.Add(Blocking(
                    TranslationIssueCode.PlaceholderChanged,
                    $"The protected token '{token}' was not preserved."));
            }
        }

        foreach (string number in ExtractNumbers(source))
        {
            if (!translation.Contains(number, StringComparison.Ordinal))
            {
                issues.Add(Blocking(
                    TranslationIssueCode.MissingData,
                    $"The source value '{number}' was not preserved."));
            }
        }

        if (LooksLikeModelOutputContamination(translation))
        {
            issues.Add(Blocking(
                TranslationIssueCode.OutputFormat,
                "The translation contains structured output or explanatory text instead of only the translation."));
        }

        return issues;
    }

    public static IReadOnlyList<TranslationIssue> NormalizeModelIssues(
        IReadOnlyList<TranslationIssue>? issues)
    {
        if (issues is null || issues.Count == 0)
        {
            return [];
        }

        return issues
            .Where(issue => !string.IsNullOrWhiteSpace(issue.Description))
            .Select(issue => SubjectiveIssueCodes.Contains(issue.Code)
                ? issue with { Severity = TranslationIssueSeverity.Warning }
                : issue)
            .ToArray();
    }

    public static TranslationIssue Blocking(
        TranslationIssueCode code,
        string description) =>
        new(TranslationIssueSeverity.Blocking, code, description);

    public static TranslationIssue Warning(
        TranslationIssueCode code,
        string description) =>
        new(TranslationIssueSeverity.Warning, code, description);

    public static void RecordBranchOperation(
        string operationName,
        TranslationBranchState state,
        TimeSpan duration,
        bool skippedForUpstreamError = false)
    {
        string outcome = skippedForUpstreamError
            ? "skipped"
            : state.ErrorType is not null
                ? state.ShouldRetry ? "retry" : "error"
                : state.ValidationIssues?.Any(issue =>
                    issue.Severity == TranslationIssueSeverity.Warning) == true
                    ? "warning"
                : "success";
        AITelemetry.RecordOperation(
            operationName,
            "workflow",
            "translation",
            outcome,
            duration,
            skippedForUpstreamError ? null : state.ErrorType,
            branchName: state.TargetLanguage);
    }

    [GeneratedRegex("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageIdentifierRegex();

    [GeneratedRegex("(?:\\{\\{[^{}]+\\}\\}|\\{\\d+\\}|%[a-zA-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex ProtectedTokenRegex();

    [GeneratedRegex("(?<![\\p{L}\\p{N}])\\d+(?:[.,]\\d+)*(?![\\p{L}\\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    private static IEnumerable<string> ExtractProtectedTokens(string text) =>
        ProtectedTokenRegex()
            .Matches(text)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal);

    private static IEnumerable<string> ExtractNumbers(string text) =>
        NumberRegex()
            .Matches(text)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal);

    private static bool LooksLikeModelOutputContamination(string translation) =>
        translation.Contains("\"issues\"", StringComparison.OrdinalIgnoreCase) ||
        translation.Contains("\"translatedText\"", StringComparison.OrdinalIgnoreCase) ||
        translation.StartsWith("{", StringComparison.Ordinal) ||
        translation.EndsWith("}", StringComparison.Ordinal) ||
        translation.Contains("corrected issues:", StringComparison.OrdinalIgnoreCase) ||
        translation.Contains("no spelling or grammar errors", StringComparison.OrdinalIgnoreCase);
}
