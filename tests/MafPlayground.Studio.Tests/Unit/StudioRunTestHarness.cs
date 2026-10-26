using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MafPlayground.AI.Abstractions;
using MafPlayground.Studio.Catalog;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Unit;

internal sealed class RunHarness(ServiceProvider provider, StudioRunService service, ControlledExecutor executor, ManualStudioClock clock) : IAsyncDisposable
{
    internal static readonly AICallerContext Caller = new("local", "tenant");
    internal StudioRunService Service => service;
    internal ManualStudioClock Clock => clock;
    internal int Count => executor.Count;
    internal static StudioRunRequest Request(string key, string? session = null) => new(key, "novel", "profile", JsonSerializer.SerializeToElement(new { value = 1 }), session);
    internal Task<StudioRunReceipt> SubmitAsync(string key, string? session = null) => service.StartAsync(Request(key, session), Caller, default).AsTask();
    internal Task<TestExecution> NextAsync() => executor.NextAsync();
    internal IReadOnlyCollection<string> DisposedScopes => executor.DisposedScopes.ToArray();
    internal void Record(string id, object data) => provider.GetRequiredService<IStudioEventSink>().Record(new(id, 0, clock.GetUtcNow(), "tool", "node", "invocation", "span", JsonSerializer.SerializeToElement(data)));
    internal static async Task<RunHarness> CreateAsync(Func<StudioOptions, StudioOptions>? configure = null)
    {
        ManualStudioClock clock = new();
        ControlledExecutor executor = new();
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(executor);
        services.AddScoped<IStudioEntityExecutor, ScopedExecutor>();
        services.AddScoped<IStudioCatalog, TestCatalog>();
        services.AddStudioRuns(configure?.Invoke(new StudioOptions()) ?? new StudioOptions());
        ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        StudioRunService service = provider.GetRequiredService<StudioRunService>();
        await service.StartAsync(default);
        return new(provider, service, executor, clock);
    }
    internal async Task<StudioRunSnapshot> TerminalAsync(string id)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        while (true)
        {
            StudioRunSnapshot? snapshot = await service.GetAsync(id, deadline.Token);
            if (snapshot is { IsTerminal: true }) return snapshot;
            await Task.Delay(1, deadline.Token);
        }
    }
    public async ValueTask DisposeAsync()
    {
        executor.Release();
        await service.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        await provider.DisposeAsync();
    }
    private sealed class ScopedExecutor(ControlledExecutor executor) : IStudioEntityExecutor, IAsyncDisposable
    {
        private readonly string id = Guid.NewGuid().ToString();
        public ValueTask<JsonElement> ExecuteAsync(StudioRunRequest request, StudioExecutionContext context, CancellationToken cancellationToken) => executor.Execute(request, context, id, cancellationToken);
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            executor.DisposedScopes.Add(id);
        }
    }
    private sealed class TestCatalog : IStudioCatalog
    {
        public ValueTask<IReadOnlyList<StudioEntityDescriptor>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<StudioEntityDescriptor>>([]);
        public ValueTask<StudioTopology> GetTopologyAsync(string entityId, string profileId, CancellationToken cancellationToken) => ValueTask.FromResult(new StudioTopology(entityId, profileId, StudioTopologyStatus.Unavailable, "test", null, [], [], null, null));
    }
}
internal sealed class ControlledExecutor
{
    private readonly Channel<TestExecution> started = Channel.CreateUnbounded<TestExecution>();
    private readonly ConcurrentBag<TestExecution> jobs = [];
    private int count;
    private volatile bool releasing;
    internal ConcurrentBag<string> DisposedScopes { get; } = [];
    internal int Count => Volatile.Read(ref count);
    internal async ValueTask<JsonElement> Execute(StudioRunRequest request, StudioExecutionContext context, string scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        TestExecution job = new(request, context, scope, token);
        jobs.Add(job);
        Interlocked.Increment(ref count);
        if (releasing) job.Complete();
        started.Writer.TryWrite(job);
        return await job.Completion.Task;
    }
    internal async Task<TestExecution> NextAsync() => await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    internal void Release()
    {
        releasing = true;
        foreach (TestExecution job in jobs) job.Complete();
    }
}
internal sealed record TestExecution(StudioRunRequest Request, StudioExecutionContext Context, string ScopeId, CancellationToken Token)
{
    internal TaskCompletionSource<JsonElement> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void Complete(JsonElement? result = null) => Completion.TrySetResult(result ?? JsonSerializer.SerializeToElement(new { ok = true }));
    internal void Cancel() => Completion.TrySetCanceled(Token);
}
internal sealed class ManualStudioClock : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now = DateTimeOffset.Parse("2026-10-25T00:00:00Z");
    public override DateTimeOffset GetUtcNow()
    {
        lock (timers) return now;
    }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (timers)
        {
            ManualTimer t = new(this, callback, state);
            timers.Add(t);
            t.Change(dueTime, period);
            return t;
        }
    }
    internal void Advance(TimeSpan duration)
    {
        List<ManualTimer> due;
        lock (timers)
        {
            now += duration;
            due = timers.Where(t => t.Due <= now).ToList();
            foreach (ManualTimer t in due) t.Due = DateTimeOffset.MaxValue;
        }
        foreach (ManualTimer t in due) t.Fire();
    }
    private sealed class ManualTimer(ManualStudioClock owner, TimerCallback callback, object? state) : ITimer
    {
        internal DateTimeOffset Due { get; set; } = DateTimeOffset.MaxValue;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.timers) Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.now + dueTime;
            return true;
        }
        internal void Fire() => callback(state);
        public void Dispose()
        {
            lock (owner.timers) owner.timers.Remove(this);
        }
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
