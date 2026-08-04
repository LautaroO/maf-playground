using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Resilience;

internal sealed class TimeoutChatClientDecorator(
    IOptions<AIResilienceOptions> options,
    TimeProvider timeProvider) : IChatClientDecorator
{
    private readonly TimeSpan _modelCallTimeout = options.Value.ModelCallTimeout;

    public IChatClient Decorate(IChatClient chatClient, AIModelSelection modelSelection)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(modelSelection);

        return new TimeoutChatClient(chatClient, _modelCallTimeout, timeProvider);
    }

    private sealed class TimeoutChatClient(
        IChatClient innerClient,
        TimeSpan modelCallTimeout,
        TimeProvider timeProvider) : DelegatingChatClient(innerClient)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? chatOptions = null,
            CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource timeoutSource =
                new(modelCallTimeout, timeProvider);
            using CancellationTokenSource linkedSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);

            try
            {
                return await base
                    .GetResponseAsync(messages, chatOptions, linkedSource.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested &&
                    timeoutSource.IsCancellationRequested)
            {
                throw CreateTimeoutException(exception);
            }
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? chatOptions = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource timeoutSource =
                new(modelCallTimeout, timeProvider);
            using CancellationTokenSource linkedSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);

            IAsyncEnumerable<ChatResponseUpdate> updates = base
                .GetStreamingResponseAsync(messages, chatOptions, linkedSource.Token);
            await using IAsyncEnumerator<ChatResponseUpdate> enumerator = updates
                .GetAsyncEnumerator(linkedSource.Token);

            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException exception)
                    when (!cancellationToken.IsCancellationRequested &&
                        timeoutSource.IsCancellationRequested)
                {
                    throw CreateTimeoutException(exception);
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }

        private TimeoutException CreateTimeoutException(OperationCanceledException innerException) =>
            new($"The model call exceeded {modelCallTimeout}.", innerException);
    }
}
