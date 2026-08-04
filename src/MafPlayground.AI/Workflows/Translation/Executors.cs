using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Workflows.Translation;

internal sealed class TranslationChatInputExecutor()
    : ChatProtocolExecutor(
        "translation-chat-input",
        new ChatProtocolExecutorOptions
        {
            StringMessageChatRole = ChatRole.User,
        },
        declareCrossRunShareable: false)
{
    protected override async ValueTask TakeTurnAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        bool? emitEvents,
        CancellationToken cancellationToken)
    {
        string text = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text
            ?? messages.LastOrDefault()?.Text
            ?? string.Empty;
        await context.SendMessageAsync(
            new TranslationWorkflowInput(text),
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
    IReadOnlyList<string> targetLanguages,
    bool emitAgentResponse)
    : Executor<ValidatedTranslationMessage>("translation-aggregate")
{
    private readonly List<ValidatedTranslationMessage> _translations = [];

    public override ValueTask HandleAsync(
        ValidatedTranslationMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _translations.Add(message);
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnMessageDeliveryFinishedAsync(
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, ValidatedTranslation> translationsByLanguage =
            _translations.ToDictionary(
                message => message.Translation.TargetLanguage,
                message => message.Translation,
                StringComparer.OrdinalIgnoreCase);
        ValidatedTranslation[] orderedTranslations = targetLanguages
            .Select(language => translationsByLanguage[language])
            .ToArray();
        string sourceText = _translations[0].SourceText;
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
