using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Guards.Content;

internal sealed class ContentGuardChatClientDecorator(
    GuardExecutionContextAccessor contextAccessor,
    ContentGuard contentGuard) : IChatClientDecorator
{
    public int Order => ChatClientDecoratorOrder.ContentGuard;

    public IChatClient Decorate(IChatClient chatClient, AIModelSelection modelSelection)
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
            CancellationToken cancellationToken = default) =>
            await base.GetResponseAsync(
                await GuardInputAsync(messages, cancellationToken).ConfigureAwait(false),
                chatOptions,
                cancellationToken).ConfigureAwait(false);

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? chatOptions = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ChatMessage> guarded = await GuardInputAsync(
                messages,
                cancellationToken).ConfigureAwait(false);
            await foreach (ChatResponseUpdate update in base
                .GetStreamingResponseAsync(guarded, chatOptions, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return update;
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
                List<AIContent> contents = [];
                if (guardedText.Length > 0)
                {
                    contents.Add(new TextContent(guardedText));
                }

                foreach (AIContent content in message.Contents)
                {
                    if (content is not TextContent)
                    {
                        contents.Add(content);
                    }
                }

                result.Add(new ChatMessage(message.Role, contents)
                {
                    AuthorName = message.AuthorName,
                    CreatedAt = message.CreatedAt,
                    MessageId = message.MessageId,
                    AdditionalProperties = message.AdditionalProperties,
                });
            }

            return result;
        }
    }
}
