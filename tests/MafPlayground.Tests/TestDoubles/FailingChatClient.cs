using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests;

internal sealed class FailingChatClient(
    Exception? exception = null,
    bool waitForCancellation = false) : IChatClient
{
    private readonly Exception _exception = exception ??
        new InvalidOperationException("Synthetic provider failure.");

    public ChatClientMetadata Metadata { get; } = new("fake");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (waitForCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        throw _exception;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (waitForCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        else
        {
            await Task.Yield();
        }

        throw _exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
