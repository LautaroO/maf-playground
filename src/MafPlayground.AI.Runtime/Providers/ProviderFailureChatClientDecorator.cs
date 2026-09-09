using System.Runtime.CompilerServices;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Contracts;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Runtime.Providers;

internal sealed class ProviderFailureChatClientDecorator : IChatClientDecorator
{
    public int Order => ChatClientDecoratorOrder.ProviderFailure;

    public IChatClient Decorate(
        IChatClient chatClient,
        AIModelSelection modelSelection,
        TimeSpan modelCallTimeout) =>
        new ProviderFailureChatClient(chatClient);

    private sealed class ProviderFailureChatClient(IChatClient innerClient)
        : DelegatingChatClient(innerClient)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.GetResponseAsync(messages, options, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new AIProviderFailureException("The AI provider call failed.", exception);
            }
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await using IAsyncEnumerator<ChatResponseUpdate> enumerator = base
                .GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new AIProviderFailureException(
                        "The AI provider streaming call failed.",
                        exception);
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }
}
