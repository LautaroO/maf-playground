using System.Diagnostics;
using System.Runtime.CompilerServices;
using MafPlayground.AI;
using MafPlayground.AI.Resilience;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests;

public sealed class TimeoutChatClientDecoratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelCall_ExceedingConfiguredTimeout_ThrowsTimeoutException(
        bool streaming)
    {
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new BlockingProvider());
        services.AddAIServices(AIModelSelection.Parse("blocking:model"));
        services.Configure<AIResilienceOptions>(options =>
            options.ModelCallTimeout = TimeSpan.FromMilliseconds(25));

        using ServiceProvider provider = services.BuildServiceProvider();
        IChatClient chatClient = provider.GetRequiredService<IChatClient>();

        if (streaming)
        {
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await foreach (ChatResponseUpdate _ in
                    chatClient.GetStreamingResponseAsync(
                        [new ChatMessage(ChatRole.User, "hello")]))
                {
                }
            });
        }
        else
        {
            await Assert.ThrowsAsync<TimeoutException>(() =>
                chatClient.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, "hello")]));
        }
    }

    private sealed class BlockingProvider : IChatClientProvider
    {
        public string Name => "blocking";

        public IChatClient CreateChatClient(string model) => new BlockingChatClient();
    }

    private sealed class BlockingChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("blocking");

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
