using System.Runtime.CompilerServices;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Contracts;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Runtime.Providers;

internal sealed class ProviderFailureChatClientDecorator(IEnumerable<IAIProviderExceptionDiagnostics> diagnostics)
    : IChatClientDecorator
{
    private readonly IAIProviderExceptionDiagnostics[] readers = diagnostics.ToArray();

    public int Order => ChatClientDecoratorOrder.ProviderFailure;

    public IChatClient Decorate(
        IChatClient chatClient,
        AIModelSelection modelSelection,
        TimeSpan modelCallTimeout) =>
        new ProviderFailureChatClient(chatClient, readers);

    private sealed class ProviderFailureChatClient(
        IChatClient innerClient, IReadOnlyList<IAIProviderExceptionDiagnostics> diagnostics)
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
                throw TranslateFailure(exception, "The AI provider call failed.");
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

        private IAsyncEnumerable<ChatResponseUpdate> CreateStream(
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

        private IAsyncEnumerator<ChatResponseUpdate> CreateEnumerator(
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

        private async ValueTask<bool> MoveNextAsync(
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

        private async ValueTask DisposeAsync(
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

        private AIProviderFailureException TranslateStreamingFailure(Exception exception) =>
            TranslateFailure(exception, "The AI provider streaming call failed.");

        private AIProviderFailureException TranslateFailure(Exception exception, string message)
        {
            AIProviderFailureDetails? details = null;
            foreach (IAIProviderExceptionDiagnostics reader in diagnostics)
            {
                try
                {
                    details = reader.Describe(exception);
                    if (details is not null) break;
                }
                catch (Exception) { /* Diagnostic enrichment must never mask the provider failure. */ }
            }
            return new AIProviderFailureException(message, exception) { Details = details };
        }
    }
}
