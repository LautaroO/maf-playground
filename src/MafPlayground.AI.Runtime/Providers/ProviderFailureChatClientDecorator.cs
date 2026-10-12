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
            IAsyncEnumerable<ChatResponseUpdate> stream = CreateStream(
                () => base.GetStreamingResponseAsync(messages, options, cancellationToken));
            IAsyncEnumerator<ChatResponseUpdate> enumerator = CreateEnumerator(
                stream,
                cancellationToken);
            try
            {
                while (await MoveNextAsync(enumerator).ConfigureAwait(false))
                {
                    yield return enumerator.Current;
                }
            }
            finally
            {
                await DisposeAsync(enumerator).ConfigureAwait(false);
            }
        }

        private static IAsyncEnumerable<ChatResponseUpdate> CreateStream(
            Func<IAsyncEnumerable<ChatResponseUpdate>> factory)
        {
            try
            {
                return factory();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw TranslateStreamingFailure(exception);
            }
        }

        private static IAsyncEnumerator<ChatResponseUpdate> CreateEnumerator(
            IAsyncEnumerable<ChatResponseUpdate> stream,
            CancellationToken cancellationToken)
        {
            try
            {
                return stream.GetAsyncEnumerator(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw TranslateStreamingFailure(exception);
            }
        }

        private static async ValueTask<bool> MoveNextAsync(
            IAsyncEnumerator<ChatResponseUpdate> enumerator)
        {
            try
            {
                return await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw TranslateStreamingFailure(exception);
            }
        }

        private static async ValueTask DisposeAsync(
            IAsyncEnumerator<ChatResponseUpdate> enumerator)
        {
            try
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw TranslateStreamingFailure(exception);
            }
        }

        private static AIProviderFailureException TranslateStreamingFailure(
            Exception exception) => new(
                "The AI provider streaming call failed.",
                exception);
    }
}
