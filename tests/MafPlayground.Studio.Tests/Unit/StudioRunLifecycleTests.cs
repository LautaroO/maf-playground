using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using Xunit;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioRunLifecycleTests
{
    [Fact]
    public async Task CancelIsNonterminalUntilAcknowledgedAndSessionLeaseRemainsHeld()
    {
        await using RunHarness host = await RunHarness.CreateAsync();
        StudioRunReceipt receipt = await host.SubmitAsync("cancel", "lease");
        TestExecution job = await host.NextAsync();
        Assert.True(await host.Service.CancelAsync(receipt.RunId, default));
        StudioRunSnapshot pending = (await host.Service.GetAsync(receipt.RunId, default))!;
        Assert.True(pending.CancellationRequested);
        Assert.Equal(StudioRunStatus.Running, pending.Status);
        Assert.True(job.Token.IsCancellationRequested);
        await Assert.ThrowsAsync<StudioSessionBusyException>(() => host.SubmitAsync("overlap", "lease"));
        job.Cancel();
        Assert.Equal(StudioRunStatus.Cancelled, (await host.TerminalAsync(receipt.RunId)).Status);
        Assert.False(await host.Service.CancelAsync(receipt.RunId, default));
        await host.SubmitAsync("next", "lease");
    }

    [Fact]
    public async Task TimeoutUsesTimeProviderAndWaitsForExecutor()
    {
        await using RunHarness host = await RunHarness.CreateAsync();
        StudioRunReceipt receipt = await host.SubmitAsync("timeout");
        TestExecution job = await host.NextAsync();
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(job.Token.IsCancellationRequested);
        Assert.True((await host.Service.GetAsync(receipt.RunId, default))!.CancellationRequested);
        Assert.Equal(StudioRunStatus.Running, (await host.Service.GetAsync(receipt.RunId, default))!.Status);
        job.Cancel();
        Assert.Equal(StudioRunStatus.TimedOut, (await host.TerminalAsync(receipt.RunId)).Status);
    }

    [Fact]
    public async Task CompletionWinningCancellationRemainsSuccessful()
    {
        await using RunHarness host = await RunHarness.CreateAsync();
        StudioRunReceipt receipt = await host.SubmitAsync("race");
        TestExecution job = await host.NextAsync();
        await host.Service.CancelAsync(receipt.RunId, default);
        job.Complete();
        StudioRunSnapshot snapshot = await host.TerminalAsync(receipt.RunId);
        Assert.True(snapshot.CancellationRequested);
        Assert.Equal(StudioRunStatus.Completed, snapshot.Status);
    }

    [Fact]
    public async Task FailureIsObservedAndWorkerContinues()
    {
        await using RunHarness host = await RunHarness.CreateAsync(options => options with { MaxConcurrentRuns = 1 });
        StudioRunReceipt receipt = await host.SubmitAsync("failure");
        (await host.NextAsync()).Completion.TrySetException(new InvalidOperationException("sensitive payload"));
        StudioRunSnapshot snapshot = await host.TerminalAsync(receipt.RunId);
        Assert.Equal(StudioRunStatus.Failed, snapshot.Status);
        Assert.Equal("execution_failed", snapshot.ErrorCategory);
        await host.SubmitAsync("next");
        (await host.NextAsync()).Complete();
    }

    [Fact]
    public async Task ShutdownCancelsAcceptedJobsAndBoundsCleanupWithTimeProvider()
    {
        await using RunHarness host = await RunHarness.CreateAsync(options => options with { MaxConcurrentRuns = 1 });
        StudioRunReceipt active = await host.SubmitAsync("active");
        TestExecution job = await host.NextAsync();
        StudioRunReceipt queued = await host.SubmitAsync("queued");
        Task stop = host.Service.StopAsync(default);
        Assert.True(job.Token.IsCancellationRequested);
        Assert.False(stop.IsCompleted);
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(StudioRunStatus.Running, (await host.Service.GetAsync(active.RunId, default))!.Status);
        await Assert.ThrowsAsync<StudioCapacityException>(() => host.SubmitAsync("stopped"));
        job.Cancel();
        Assert.Equal(StudioRunStatus.Cancelled, (await host.TerminalAsync(active.RunId)).Status);
        Assert.Equal(StudioRunStatus.Cancelled, (await host.TerminalAsync(queued.RunId)).Status);
        Assert.Equal(1, host.Count);
    }

    [Fact]
    public async Task InvalidExecutorResultFailsWithoutLosingWorkerOrSession()
    {
        await using RunHarness host = await RunHarness.CreateAsync(options => options with { MaxConcurrentRuns = 1 });
        StudioRunReceipt receipt = await host.SubmitAsync("invalid", "lease");
        (await host.NextAsync()).Completion.TrySetResult(default);
        StudioRunSnapshot snapshot = await host.TerminalAsync(receipt.RunId);
        Assert.Equal(StudioRunStatus.Failed, snapshot.Status);
        Assert.Equal("invalid_result", snapshot.ErrorCategory);
        await host.SubmitAsync("valid", "lease");
        (await host.NextAsync()).Complete();
    }

    [Fact]
    public async Task BlockingCancellationCallbackCannotExceedShutdownGrace()
    {
        await using RunHarness host = await RunHarness.CreateAsync();
        await host.SubmitAsync("callback");
        TestExecution job = await host.NextAsync();
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = job.Token.Register(() => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); });
        Task stop = Task.Run(() => host.Service.StopAsync(default));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        try
        {
            await stop.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            release.Set();
            job.Cancel();
        }
    }

    [Fact]
    public async Task ThrowingCancellationCallbackIsObservedAndDoesNotKillWorker()
    {
        await using RunHarness host = await RunHarness.CreateAsync(options => options with { MaxConcurrentRuns = 1 });
        StudioRunReceipt receipt = await host.SubmitAsync("throwing-callback");
        TestExecution job = await host.NextAsync();
        using CancellationTokenRegistration registration = job.Token.Register(() => throw new InvalidOperationException("callback failed"));
        Assert.True(await host.Service.CancelAsync(receipt.RunId, default));
        job.Cancel();
        Assert.Equal(StudioRunStatus.Cancelled, (await host.TerminalAsync(receipt.RunId)).Status);
        await host.SubmitAsync("after-callback");
        (await host.NextAsync()).Complete();
    }
}
