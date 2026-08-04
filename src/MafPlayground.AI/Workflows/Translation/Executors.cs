using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Workflows.Translation;

internal sealed class TranslationInputExecutor(
    TranslationWorkflowOptions options)
    : Executor<TranslationWorkflowInput, TranslationWorkflowRequest>(
        "translation-input",
        declareCrossRunShareable: true)
{
    public override ValueTask<TranslationWorkflowRequest> HandleAsync(
        TranslationWorkflowInput message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(TranslationWorkflowHelpers.ValidateRequest(
            new TranslationWorkflowRequest(
                message.Text,
                message.TargetLanguages),
            options));
}

internal sealed class TranslationExecutor(
    string id,
    string targetLanguage,
    TranslationService translationService)
    : Executor(id, declareCrossRunShareable: false)
{
    private async ValueTask HandleInitialRequestAsync(
        TranslationWorkflowRequest request,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        TranslationBranchState state = await translationService.TranslateAsync(
            new TranslationBranchState(
                request.Text,
                request.TargetLanguages,
                targetLanguage),
            cancellationToken);
        await context.SendMessageAsync(state, cancellationToken);
    }

    private async ValueTask HandleRetryAsync(
        TranslationBranchState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        TranslationBranchState translatedState = await translationService.TranslateAsync(
            state,
            cancellationToken);
        await context.SendMessageAsync(translatedState, cancellationToken);
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes =>
            {
                routes.AddHandler<TranslationWorkflowRequest>(HandleInitialRequestAsync);
                routes.AddHandler<TranslationBranchState>(HandleRetryAsync);
            })
            .SendsMessage<TranslationBranchState>();
}

internal sealed class TranslationValidationExecutor(
    string id,
    string translatorId,
    TranslationService translationService)
    : Executor(id, declareCrossRunShareable: false)
{
    private async ValueTask HandleAsync(
        TranslationBranchState state,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        TranslationBranchState validatedState = await translationService.ValidateAsync(
            state,
            cancellationToken);
        if (validatedState.ShouldRetry)
        {
            await context.SendMessageAsync(
                validatedState,
                translatorId,
                cancellationToken);
            return;
        }

        await context.SendMessageAsync(
            new ValidatedTranslationMessage(
                validatedState.SourceText,
                validatedState.RequestedTargetLanguages,
                TranslationService.Complete(validatedState)),
            TranslationAggregatorExecutor.ExecutorId,
            cancellationToken);
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes =>
                routes.AddHandler<TranslationBranchState>(HandleAsync))
            .SendsMessage<TranslationBranchState>()
            .SendsMessage<ValidatedTranslationMessage>();
}

internal sealed class TranslationChatInputExecutor()
    : ChatProtocolExecutor(
        "translation-chat-input",
        new ChatProtocolExecutorOptions
        {
            StringMessageChatRole = ChatRole.User,
        },
        declareCrossRunShareable: false)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    protected override async ValueTask TakeTurnAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        bool? emitEvents,
        CancellationToken cancellationToken)
    {
        string json = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text
            ?? messages.LastOrDefault()?.Text
            ?? string.Empty;

        TranslationWorkflowInput input;
        try
        {
            input = JsonSerializer.Deserialize<TranslationWorkflowInput>(
                    json,
                    SerializerOptions)
                ?? throw new ArgumentException(
                    "The workflow input must be a JSON object with text and targetLanguages.");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The workflow input must be a JSON object with text and targetLanguages.",
                exception);
        }

        await context.SendMessageAsync(
            input,
            cancellationToken);
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
    {
        base.ConfigureProtocol(protocolBuilder);
        return protocolBuilder.SendsMessage<TranslationWorkflowInput>();
    }
}

[YieldsOutput(typeof(TranslationWorkflowResult))]
[YieldsOutput(typeof(ChatMessage))]
internal sealed class TranslationAggregatorExecutor(
    bool emitAgentResponse)
    : Executor<ValidatedTranslationMessage>(ExecutorId)
{
    public const string ExecutorId = "translation-aggregate";

    private readonly Dictionary<string, ValidatedTranslationMessage> _translations =
        new(StringComparer.OrdinalIgnoreCase);

    public override async ValueTask HandleAsync(
        ValidatedTranslationMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_translations.TryAdd(message.Translation.TargetLanguage, message))
        {
            throw new InvalidOperationException(
                $"Translation '{message.Translation.TargetLanguage}' completed more than once.");
        }

        if (_translations.Count < message.RequestedTargetLanguages.Count)
        {
            return;
        }

        ValidatedTranslation[] orderedTranslations = message.RequestedTargetLanguages
            .Select(language => _translations[language].Translation)
            .ToArray();
        ValidatedTranslationMessage firstTranslation = _translations.Values.First();
        string sourceText = firstTranslation.SourceText;
        _translations.Clear();

        TranslationWorkflowResult result = new(sourceText, orderedTranslations);
        await context.YieldOutputAsync(result, cancellationToken);

        if (emitAgentResponse)
        {
            string json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
            string responseId = $"translation-{Guid.NewGuid():N}";
            AgentResponseUpdate update = new(ChatRole.Assistant, json)
            {
                AgentId = "translation-workflow",
                ResponseId = responseId,
                MessageId = responseId,
            };
            await context.AddEventAsync(
                new AgentResponseUpdateEvent("translation-aggregate", update),
                cancellationToken);
            await context.YieldOutputAsync(
                new ChatMessage(ChatRole.Assistant, json),
                cancellationToken);
        }
    }
}
