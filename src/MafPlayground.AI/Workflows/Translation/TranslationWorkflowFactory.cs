using System.Text.RegularExpressions;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Workflows.Translation;

public sealed partial class TranslationWorkflowFactory(
    TranslationBranchProcessor branchProcessor,
    IOptions<TranslationWorkflowOptions> options,
    IOptions<AgentTelemetryOptions> telemetryOptions)
{
    private readonly TranslationWorkflowOptions _options = options.Value;
    private readonly AgentTelemetryOptions _telemetryOptions = telemetryOptions.Value;

    public Workflow Create(IReadOnlyList<string> targetLanguages) =>
        Create(targetLanguages, "translation-workflow", useChatProtocol: false);

    public Workflow CreateForDevUI(
        IReadOnlyList<string> targetLanguages,
        string workflowName = "translation-workflow") =>
        Create(targetLanguages, workflowName, useChatProtocol: true);

    private Workflow Create(
        IReadOnlyList<string> targetLanguages,
        string workflowName,
        bool useChatProtocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        string[] validatedLanguages = ValidateLanguages(targetLanguages);

        ExecutorBinding input = ((Func<TranslationWorkflowInput,
            TranslationWorkflowRequest>)(workflowInput => Validate(
                new TranslationWorkflowRequest(
                    workflowInput.Text,
                    validatedLanguages))))
            .BindAsExecutor<TranslationWorkflowInput, TranslationWorkflowRequest>(
                "translation-input");
        List<ExecutorBinding> translators = [];
        List<ExecutorBinding> validators = [];

        foreach (string language in validatedLanguages)
        {
            string executorSuffix = NormalizeExecutorId(language);
            ExecutorBinding translator =
                ((Func<TranslationWorkflowRequest, CancellationToken,
                    ValueTask<TranslationCandidate>>)((input, cancellationToken) =>
                        branchProcessor.TranslateAsync(input, language, cancellationToken)))
                .BindAsExecutor<TranslationWorkflowRequest, TranslationCandidate>(
                    $"translate-{executorSuffix}");
            ExecutorBinding validator =
                ((Func<TranslationCandidate, CancellationToken,
                    ValueTask<ValidatedTranslationMessage>>)(async (candidate, cancellationToken) =>
                        new ValidatedTranslationMessage(
                            candidate.SourceText,
                            await branchProcessor.ValidateAndRepairAsync(
                                candidate,
                                cancellationToken))))
                .BindAsExecutor<TranslationCandidate, ValidatedTranslationMessage>(
                    $"validate-{executorSuffix}");

            translators.Add(translator);
            validators.Add(validator);
        }

        TranslationAggregatorExecutor aggregator = new(
            validatedLanguages,
            emitAgentResponse: useChatProtocol);
        WorkflowBuilder builder;
        if (useChatProtocol)
        {
            TranslationChatInputExecutor chatInput = new();
            builder = new WorkflowBuilder(chatInput)
                .AddEdge(chatInput, input);
        }
        else
        {
            builder = new WorkflowBuilder(input);
        }

        builder
            .WithName(workflowName)
            .WithDescription(
                "Translates text into multiple target languages in parallel, " +
                "validates each translation, and repairs invalid results with bounded retries.")
            .WithOpenTelemetry(telemetry =>
                telemetry.EnableSensitiveData = _telemetryOptions.EnableSensitiveData)
            .AddFanOutEdge(input, translators);

        for (int index = 0; index < translators.Count; index++)
        {
            builder.AddEdge(translators[index], validators[index]);
        }

        return builder
            .AddFanInBarrierEdge(validators, aggregator)
            .WithOutputFrom(aggregator)
            .Build();
    }

    public TranslationWorkflowRequest Validate(TranslationWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new ArgumentException("Translation text is required.", nameof(request));
        }

        if (text.Length > _options.MaxInputCharacters)
        {
            throw new ArgumentException(
                $"Translation text cannot exceed {_options.MaxInputCharacters} characters.",
                nameof(request));
        }

        string[] languages = ValidateLanguages(request.TargetLanguages);
        return new TranslationWorkflowRequest(text, languages);
    }

    private string[] ValidateLanguages(IReadOnlyList<string>? targetLanguages)
    {
        string[] languages = targetLanguages?
            .Select(language => language?.Trim() ?? string.Empty)
            .Where(language => language.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (languages.Length == 0)
        {
            throw new ArgumentException(
                "At least one target language is required.",
                nameof(targetLanguages));
        }

        if (languages.Length > _options.MaxTargetLanguages)
        {
            throw new ArgumentException(
                $"A maximum of {_options.MaxTargetLanguages} target languages is allowed.",
                nameof(targetLanguages));
        }

        string? invalidLanguage = languages.FirstOrDefault(
            language => !LanguageIdentifierRegex().IsMatch(language));
        if (invalidLanguage is not null)
        {
            throw new ArgumentException(
                $"'{invalidLanguage}' is not a valid language identifier.",
                nameof(targetLanguages));
        }

        return languages;
    }

    private static string NormalizeExecutorId(string language) =>
        language.ToLowerInvariant().Replace('-', '_');

    [GeneratedRegex("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageIdentifierRegex();
}
