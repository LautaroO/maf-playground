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
