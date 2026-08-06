using System.Text.RegularExpressions;

namespace MafPlayground.AI.Workflows.Translation;

internal static partial class TranslationWorkflowHelpers
{
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
}
