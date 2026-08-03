using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests;

internal sealed class FakeChatClient(string responseText) : IChatClient
{
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public ChatClientMetadata Metadata { get; } = new("fake");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate(ChatRole.Assistant, responseText);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
