using System.Runtime.CompilerServices;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Observability;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Workflows.Translation;

public sealed class TranslationWorkflowFactory(
    TranslationService translationService,
    IOptions<TranslationWorkflowOptions> options,
    IOptions<AgentTelemetryOptions> telemetryOptions,
    WorkflowGuardCoordinator guards)
{
    private readonly TranslationWorkflowOptions _options = options.Value;
    private readonly AgentTelemetryOptions _telemetryOptions = telemetryOptions.Value;

    public Workflow Create() =>
        Create("translation-workflow", useChatProtocol: false);

    public Workflow CreateForDevUI(
        string workflowName = "translation-workflow") =>
        Create(workflowName, useChatProtocol: true);

    private Workflow Create(
        string workflowName,
        bool useChatProtocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        string[] supportedLanguages = TranslationWorkflowHelpers.ValidateSupportedLanguages(
            _options.SupportedTargetLanguages);

        TranslationInputExecutor input = new(_options, guards);
        List<ExecutorBinding> translators = [];
        List<ExecutorBinding> validators = [];
        TranslationAggregatorExecutor aggregator = new(
            emitAgentResponse: useChatProtocol,
            guards);

        foreach (string language in supportedLanguages)
        {
            string executorSuffix = TranslationWorkflowHelpers.NormalizeExecutorId(language);
            string translatorId = $"translate-{executorSuffix}";
            TranslationExecutor translator = new(
                translatorId,
                language,
                translationService);
            TranslationValidationExecutor validator = new(
                $"validate-{executorSuffix}",
                translatorId,
                translationService);

            translators.Add(translator);
            validators.Add(validator);
        }

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
                "validates each translation, and retries invalid results with validator feedback.")
            .WithOpenTelemetry(telemetry =>
                telemetry.EnableSensitiveData = _telemetryOptions.EnableSensitiveData)
            .AddFanOutEdge<GuardedTranslationRequest>(
                input,
                translators,
                (request, _) => TranslationWorkflowHelpers.SelectTargetIndexes(
                    request?.Request ?? throw new ArgumentNullException(nameof(request)),
                    supportedLanguages));

        for (int index = 0; index < translators.Count; index++)
        {
            builder.AddEdge(translators[index], validators[index]);
            builder.AddEdge(
                validators[index],
                translators[index],
                "retry with feedback",
                idempotent: false);
            builder.AddEdge(
                validators[index],
                aggregator,
                "complete",
                idempotent: false);
        }

        return builder
            .WithOutputFrom(aggregator)
            .Build();
    }

    public TranslationWorkflowRequest Validate(TranslationWorkflowRequest request) =>
        TranslationWorkflowHelpers.ValidateRequest(request, _options);
}

public sealed class TranslationWorkflowRunner(TranslationWorkflowFactory workflowFactory)
{
    public async Task<TranslationWorkflowResult> RunAsync(
        TranslationWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        TranslationWorkflowRequest validatedRequest = workflowFactory.Validate(request);
        Workflow workflow = workflowFactory.Create();
        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            new TranslationWorkflowInput(
                validatedRequest.Text,
                validatedRequest.TargetLanguages),
            cancellationToken: cancellationToken);

        TranslationWorkflowResult? result = run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.As<TranslationWorkflowResult>())
            .LastOrDefault(output => output is not null);
        return result ?? throw new InvalidOperationException(
            "The translation workflow completed without producing a result.");
    }

    public async IAsyncEnumerable<WorkflowEvent> RunStreamingAsync(
        TranslationWorkflowRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        TranslationWorkflowRequest validatedRequest = workflowFactory.Validate(request);
        Workflow workflow = workflowFactory.Create();
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow,
            new TranslationWorkflowInput(
                validatedRequest.Text,
                validatedRequest.TargetLanguages),
            cancellationToken: cancellationToken);

        await foreach (WorkflowEvent workflowEvent in
                       run.WatchStreamAsync(cancellationToken))
        {
            yield return workflowEvent;
        }
    }
}
