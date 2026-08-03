using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace MafPlayground.Tests;

internal sealed class FakeChatClient(string responseText, UsageDetails? usage = null) : IChatClient
{
    private readonly Queue<string> _responses = new([responseText]);

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public List<ChatOptions?> RequestOptions { get; } = [];

    public ChatClientMetadata Metadata { get; } = new("fake");

    public void EnqueueResponse(string value) => _responses.Enqueue(value);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());
        RequestOptions.Add(options);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, NextResponse()))
        {
            Usage = usage,
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());
        RequestOptions.Add(options);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate(ChatRole.Assistant, NextResponse());

        if (usage is not null)
        {
            yield return new ChatResponseUpdate
            {
                Contents = [new UsageContent(usage)],
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private string NextResponse() =>
        _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
}
