using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Contracts;
using MafPlayground.AI.Context;
using MafPlayground.Studio.Catalog;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Component;

internal sealed class StudioExecutionFixture : IAsyncDisposable
{
    internal readonly RecordingClient Client = new();
    internal readonly EventCollector Events = new();
    internal ServiceProvider Provider { get; }
    internal StudioExecutionFixture(Action<IServiceCollection>? configure = null)
    {
        ServiceCollection services = new();
        services.AddSingleton<IChatClientProvider>(new ClientProvider(Client));
        services.AddSingleton<IUserContextAccessor>(new EmptyUser());
        services.AddAIRuntime(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["AI:Defaults:Model"] = "fake:default" }).Build()).AddMafPlaygroundSampleEntities();
        services.AddInMemoryAgentSessions();
        services.AddSingleton<IStudioEventSink>(Events);
        services.AddStudioCatalog().AddStudioRuns();
        configure?.Invoke(services);
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
    internal async Task<StudioEntityExecutionResult> RunAsync(object input, string entity = "basic-agent", string profile = "cli-default", string? session = null, CancellationToken token = default)
    {
        await using var scope = Provider.CreateAsyncScope();
        var request = new StudioRunRequest(Guid.NewGuid().ToString(), entity, profile, JsonSerializer.SerializeToElement(input), session);
        return await scope.ServiceProvider.GetRequiredService<IStudioEntityExecutor>().ExecuteAsync(request,
            new(Guid.NewGuid().ToString(), entity, profile, session, new("tester")), token);
    }
    public ValueTask DisposeAsync() => Provider.DisposeAsync();
    private sealed class EmptyUser : IUserContextAccessor
    {
        public UserContext GetCurrent() => new([]);
    }
    internal sealed class ClientProvider(RecordingClient client) : IChatClientProvider
    {
        public string Name => "fake";
        public IChatClient CreateChatClient(string model) { client.Models.Enqueue(model); return client; }
    }
    internal sealed class EventCollector : IStudioEventSink
    {
        internal ConcurrentQueue<StudioEvent> Items { get; } = new();
        public void Record(StudioEvent studioEvent) => Items.Enqueue(studioEvent);
    }
    internal sealed class RecordingClient : IChatClient
    {
        internal int DisposeCount;
        internal ConcurrentQueue<string> Models { get; } = new();
        internal ConcurrentQueue<ChatMessage[]> Requests { get; } = new();
        internal Func<CancellationToken, Task>? AfterFirstDelta { get; set; }
        internal Func<ChatMessage[], ChatOptions?, CancellationToken, Task<ChatResponse>>? Respond { get; set; }
        internal Func<ChatMessage[], CancellationToken, Task<string>> Reply { get; set; } = (_, _) => Task.FromResult("hello world");
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var input = messages.ToArray(); Requests.Enqueue(input);
            return Respond is null ? new(new ChatMessage(ChatRole.Assistant, await Reply(input, cancellationToken))) : await Respond(input, options, cancellationToken);
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var input = messages.ToArray(); Requests.Enqueue(input);
            if (Respond is not null)
            {
                ChatResponse response = await Respond(input, options, cancellationToken);
                yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = response.Messages.SelectMany(message => message.Contents).ToList(), FinishReason = response.FinishReason };
                yield break;
            }
            string text = await Reply(input, cancellationToken);
            yield return new(ChatRole.Assistant, text[..(text.Length / 2)]);
            if (AfterFirstDelta is not null) await AfterFirstDelta(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(ChatRole.Assistant, text[(text.Length / 2)..]);
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }
}
