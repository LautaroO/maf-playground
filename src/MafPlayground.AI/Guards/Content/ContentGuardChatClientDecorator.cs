using System.Runtime.CompilerServices;
using MafPlayground.AI.Contracts;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Guards.Content;

internal sealed class ContentGuardChatClientDecorator(
    GuardExecutionContextAccessor contextAccessor,
    ContentGuard contentGuard) : IChatClientDecorator
{
    public int Order => ChatClientDecoratorOrder.ContentGuard;

    public IChatClient Decorate(
        IChatClient chatClient,
        AIModelSelection modelSelection,
        TimeSpan modelCallTimeout)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        return new ContentGuardChatClient(chatClient, contextAccessor, contentGuard);
    }

    private sealed class ContentGuardChatClient(
        IChatClient innerClient,
        GuardExecutionContextAccessor contextAccessor,
        ContentGuard contentGuard) : DelegatingChatClient(innerClient)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? chatOptions = null,
            CancellationToken cancellationToken = default)
        {
            ChatResponse response = await base.GetResponseAsync(
                await GuardInputAsync(messages, cancellationToken).ConfigureAwait(false),
                chatOptions,
                cancellationToken).ConfigureAwait(false);
            await GuardOutputAsync(response, cancellationToken).ConfigureAwait(false);
            return response;
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? chatOptions = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ChatMessage> guarded = await GuardInputAsync(
                messages,
                cancellationToken).ConfigureAwait(false);
            GuardExecutionContext? execution = contextAccessor.Current;
            IAsyncEnumerable<ChatResponseUpdate> updates = base.GetStreamingResponseAsync(
                guarded,
                chatOptions,
                cancellationToken);
            if (execution?.Profile.Content.Enabled != true)
            {
                await foreach (ChatResponseUpdate update in updates
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    yield return update;
                }

                yield break;
            }

            // Inspect the complete response so protected values split across chunks cannot
            // bypass the content policy. MAF receives only the guarded updates for history.
            ChatResponse response = await updates.ToChatResponseAsync(cancellationToken)
                .ConfigureAwait(false);
            await GuardOutputAsync(response, cancellationToken).ConfigureAwait(false);
            foreach (ChatResponseUpdate update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        private async ValueTask GuardOutputAsync(
            ChatResponse response,
            CancellationToken cancellationToken)
        {
            GuardExecutionContext? execution = contextAccessor.Current;
            if (execution?.Profile.Content.Enabled != true)
            {
                return;
            }

            for (int index = 0; index < response.Messages.Count; index++)
            {
                response.Messages[index] = await TransformMessageAsync(
                    response.Messages[index],
                    execution.Profile.Content.OutputAction,
                    ContentOrigin.AgentOutput,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask<IReadOnlyList<ChatMessage>> GuardInputAsync(
            IEnumerable<ChatMessage> messages,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<ChatMessage> buffered = messages as IReadOnlyList<ChatMessage>
                ?? messages.ToArray();
            GuardExecutionContext? execution = contextAccessor.Current;
            if (execution?.Profile.Content.Enabled != true)
            {
                return buffered;
            }

            List<ChatMessage> result = new(buffered.Count);
            foreach (ChatMessage message in buffered)
            {
                if (message.Role != ChatRole.User)
                {
                    result.Add(message);
                    continue;
                }

                string guardedText = await contentGuard.ApplyAsync(
                    message.Text,
                    execution.Profile.Content.InputAction,
                    ContentOrigin.UserInput,
                    cancellationToken).ConfigureAwait(false);
                result.Add(CloneWithText(message, guardedText));
            }

            return result;
        }

        private async ValueTask<ChatMessage> TransformMessageAsync(
            ChatMessage message,
            GuardAction action,
            ContentOrigin origin,
            CancellationToken cancellationToken)
        {
            string guardedText = await contentGuard.ApplyAsync(
                message.Text,
                action,
                origin,
                cancellationToken).ConfigureAwait(false);
            return CloneWithText(message, guardedText);
        }

        private static ChatMessage CloneWithText(ChatMessage message, string text)
        {
            List<AIContent> contents = [];
            if (text.Length > 0)
            {
                contents.Add(new TextContent(text));
            }

            foreach (AIContent content in message.Contents)
            {
                if (content is not TextContent)
                {
                    contents.Add(content);
                }
            }

            return new ChatMessage(message.Role, contents)
            {
                AuthorName = message.AuthorName,
                CreatedAt = message.CreatedAt,
                MessageId = message.MessageId,
                AdditionalProperties = message.AdditionalProperties,
            };
        }
    }
}
