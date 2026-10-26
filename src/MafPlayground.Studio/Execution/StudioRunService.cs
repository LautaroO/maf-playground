using System.Runtime.CompilerServices;
using MafPlayground.Studio.Capture;
using MafPlayground.AI.Contracts;
using System.Text.Json;
using System.Threading.Channels;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.Studio.Catalog;
using MafPlayground.Studio.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.Studio.Execution;

public sealed class StudioRunService : IStudioRunService, IStudioEventSink, IHostedService
{
    private readonly IServiceScopeFactory scopes;
    private readonly StudioOptions options;
    private readonly TimeProvider clock;
    private readonly StudioRunStore store;
    private readonly StudioPayloadStore payloads;
    private readonly Channel<StudioRunStore.RunState> queue;
    private Task workers = Task.CompletedTask;
    private bool started;
    private bool stopping;

    public StudioRunService(IServiceScopeFactory scopes, StudioOptions options, TimeProvider clock)
    {
        Validate(options);
        this.scopes = scopes;
        this.options = options;
        this.clock = clock;
        store = new(options, clock);
        payloads = new(options);
        queue = Channel.CreateBounded<StudioRunStore.RunState>(new BoundedChannelOptions(options.QueueCapacity)
        { FullMode = BoundedChannelFullMode.Wait, SingleWriter = false, SingleReader = false, AllowSynchronousContinuations = false });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (store.Gate)
        {
            if (stopping) throw new StudioCapacityException("The Studio host is stopping.");
            if (!started)
            {
                started = true;
                workers = Task.WhenAll(Enumerable.Range(0, options.MaxConcurrentRuns).Select(_ => WorkerAsync()));
            }
        }
        return Task.CompletedTask;
    }

    public ValueTask<StudioRunReceipt> StartAsync(StudioRunRequest request, AICallerContext caller, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProfileId);
        if (request.SessionId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
        if (request.Input.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Input must be JSON.", nameof(request));
        lock (store.Gate)
        {
            StudioRunReceipt? retry = store.FindRetry(request, caller);
            if (retry is not null) return ValueTask.FromResult(retry);
            store.CheckSession(request.SessionId);
            if (!started || stopping || queue.Reader.Count >= options.QueueCapacity)
                throw new StudioCapacityException("The Studio execution queue is full or stopped.");
            StudioRunStore.RunState run = store.Add(request, caller);
            // Admission and channel completion use the same lock. This cannot fail after acceptance.
            if (!queue.Writer.TryWrite(run)) throw new InvalidOperationException("Accepted run could not be queued.");
            return ValueTask.FromResult(run.Receipt);
        }
    }

    public ValueTask<StudioRunSnapshot?> GetAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        lock (store.Gate) return ValueTask.FromResult(store.Find(runId) is { } run ? store.Snapshot(run) : null);
    }

    public ValueTask<bool> CancelAsync(string runId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        lock (store.Gate)
        {
            StudioRunStore.RunState? run = store.Find(runId);
            return ValueTask.FromResult(run is not null && BeginCancellation(run));
        }
    }

    public ValueTask<StudioEventPage> ReadEventsAsync(string runId, long after, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCursor(runId, after);
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        lock (store.Gate) return ValueTask.FromResult(Required(runId).Journal.Read(after, limit));
    }

    public async IAsyncEnumerable<StudioEvent> WatchAsync(string runId, long after, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateCursor(runId, after);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StudioEventPage page;
            Task signal;
            lock (store.Gate)
            {
                StudioRunStore.RunState? run = store.Find(runId);
                if (run is null) yield break;
                page = run.Journal.Read(after, 1);
                signal = run.Journal.Changed.Task;
            }
            if (page.HasGap)
                yield return new(runId, Math.Max(after, page.EarliestRetainedSequence - 1), clock.GetUtcNow(), "journal_gap", null, null, null,
                    JsonSerializer.SerializeToElement(new { page.EarliestRetainedSequence, page.NextCursor }));
            foreach (StudioEvent item in page.Events) yield return item;
            if (page.NextCursor > after)
            {
                after = page.NextCursor;
                continue;
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Record(StudioEvent studioEvent)
    {
        ArgumentNullException.ThrowIfNull(studioEvent);
        lock (store.Gate) store.Record(studioEvent);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource grace = new(options.ShutdownGracePeriod, clock);
        using CancellationTokenSource cleanup = CancellationTokenSource.CreateLinkedTokenSource(grace.Token, cancellationToken);
        lock (store.Gate)
        {
            stopping = true;
            queue.Writer.TryComplete();
            foreach (StudioRunStore.RunState run in store.Active()) BeginCancellation(run);
        }
        try
        {
            await workers.WaitAsync(cleanup.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (grace.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        { /* Workers retain ownership and observe late executor completion. */ }
    }

    private async Task WorkerAsync()
    {
        await foreach (StudioRunStore.RunState run in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            StudioRunStatus status = StudioRunStatus.Completed;
            JsonElement? result = null;
            StudioOutcomeClassification outcome = StudioOutcomeClassification.Unclassified;
            string? error = null;
            using ITimer timeout = clock.CreateTimer(_ =>
            {
                BeginCancellation(run, timedOut: true);
            }, null, options.RunTimeout, Timeout.InfiniteTimeSpan);
            // Timeout, explicit cancellation, and shutdown converge on this job-owned source.
            CancellationToken executionToken = run.Cancellation.Token;
            AsyncServiceScope? scope = null;
            try
            {
                executionToken.ThrowIfCancellationRequested();
                lock (store.Gate) store.Running(run);
                scope = scopes.CreateAsyncScope();
                StudioExecutionProfileBinder? binding = scope.Value.ServiceProvider.GetService<IAIEntityInspection>() is null
                    ? null : scope.Value.ServiceProvider.GetRequiredService<StudioExecutionProfileBinder>();
                if (binding is not null)
                {
                    StudioExecutionProfileSnapshot profile = binding.BindForEntity(run.ExecutionRequest!.EntityId, run.ExecutionRequest!.ProfileId);
                    lock (store.Gate) store.SetProfile(run, profile);
                }
                IStudioCatalog catalog = scope.Value.ServiceProvider.GetRequiredService<IStudioCatalog>();
                StudioTopology topology = await catalog.GetTopologyAsync(run.ExecutionRequest!.EntityId, run.ExecutionRequest!.ProfileId, executionToken).ConfigureAwait(false);
                lock (store.Gate) store.SetTopology(run, topology);
                IStudioEntityExecutor executor = scope.Value.ServiceProvider.GetRequiredService<IStudioEntityExecutor>();
                StudioEntityExecutionResult execution = await executor.ExecuteAsync(run.ExecutionRequest!,
                    new(run.Receipt.RunId, run.ExecutionRequest!.EntityId, run.ExecutionRequest!.ProfileId, run.ExecutionRequest!.SessionId, run.Caller), executionToken).ConfigureAwait(false);
                JsonElement output = execution.Output;
                if (output.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("Executor returned no JSON value.");
                result = output.Clone();
                outcome = execution.Classification;
                status = outcome switch
                {
                    StudioOutcomeClassification.PartiallyFailed => StudioRunStatus.PartiallyFailed,
                    StudioOutcomeClassification.Failed => StudioRunStatus.Failed,
                    _ => StudioRunStatus.Completed,
                };
            }
            catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
            {
                lock (store.Gate) status = run.TimeoutRequested ? StudioRunStatus.TimedOut : StudioRunStatus.Cancelled;
            }
            catch (InvalidDataException)
            {
                result = null;
                status = StudioRunStatus.Failed;
                error = "invalid_result";
            }
            catch (Exception exception)
            {
                StudioPayload errorPayload = payloads.Capture(exception, "application/json");
                Record(new(run.Receipt.RunId, 0, clock.GetUtcNow(), "execution_error", null, null, null, errorPayload.Value, errorPayload.State));
                status = StudioRunStatus.Failed;
                error = "execution_failed";
            }
            Task? cancellationWork;
            lock (store.Gate)
            {
                // Closing cancellation admission and capturing its work are one transition.
                // No callback can start once scoped resources begin disposal.
                run.Finalizing = true;
                cancellationWork = run.CancellationWork;
                if (status is StudioRunStatus.Cancelled or StudioRunStatus.TimedOut)
                    status = run.TimeoutRequested ? StudioRunStatus.TimedOut : StudioRunStatus.Cancelled;
            }
            if (cancellationWork is not null) await cancellationWork.ConfigureAwait(false);
            try
            {
                if (scope is { } executionScope) await executionScope.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                StudioPayload errorPayload = payloads.Capture(exception, "application/json");
                Record(new(run.Receipt.RunId, 0, clock.GetUtcNow(), "execution_error", null, null, null, errorPayload.Value, errorPayload.State));
                status = StudioRunStatus.Failed;
                error = "execution_failed";
            }
            lock (store.Gate) store.Finish(run, status, result, error, outcome);
        }
    }

    private StudioRunStore.RunState Required(string id) => store.Find(id) ?? throw new KeyNotFoundException("The run is unknown or evicted.");
    private bool BeginCancellation(StudioRunStore.RunState run, bool timedOut = false)
    {
        lock (store.Gate)
        {
            if (run.IsTerminal || run.Finalizing) return false;
            if (timedOut) run.TimeoutRequested = true;
            store.RequestCancellation(run);
            // Registration precedes release of the acceptance gate, including for shutdown.
            run.CancellationWork ??= ObserveCancellationAsync(run);
            return true;
        }
    }

    private static async Task ObserveCancellationAsync(StudioRunStore.RunState run)
    {
        try
        {
            await run.Cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (AggregateException)
        { /* Callback failures are observed;
         execution still owns its terminal result. */
        }
    }
    private static void ValidateCursor(string id, long after)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (after < 0) throw new ArgumentOutOfRangeException(nameof(after));
    }
    private static void Validate(StudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxConcurrentRuns < 1 || options.QueueCapacity < 1 || options.MaxCompletedRuns < 1 ||
            options.MaxCaptureBytes < 256 || options.MaxPayloadBytes < 64 || options.MaxEventsPerRun < 1 ||
            options.MaxRequestRecords < 1 || options.MaxMetadataBytes < 512 || options.RunTimeout <= TimeSpan.Zero ||
            options.ShutdownGracePeriod <= TimeSpan.Zero)
            throw new ArgumentException("Studio limits and timeouts must be positive and capture budgets must fit metadata.", nameof(options));
    }
}

public static class StudioRunServiceExtensions
{
    public static IServiceCollection AddStudioRuns(this IServiceCollection services, StudioOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(options ?? new StudioOptions());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<StudioPayloadStore>();
        services.TryAddSingleton<StudioActivityListener>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IChatClientDecorator, StudioChatClientDecorator>());
        services.TryAddScoped<StudioExecutionProfileBinder>();
        services.TryAddScoped<IStudioEntityExecutor, StudioEntityExecutor>();
        services.TryAddSingleton<StudioRunService>();
        services.TryAddSingleton<IStudioRunService>(sp => sp.GetRequiredService<StudioRunService>());
        services.TryAddSingleton<IStudioEventSink>(sp => sp.GetRequiredService<StudioRunService>());
        services.AddHostedService(sp => sp.GetRequiredService<StudioRunService>());
        return services;
    }
}
