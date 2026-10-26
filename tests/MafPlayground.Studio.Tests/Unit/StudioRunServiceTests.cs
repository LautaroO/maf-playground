using System.Text.Json;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using Xunit;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioRunServiceTests
{
    [Fact]
    public async Task ConcurrentRetriesExecuteOnceAndRejectChangedInput()
    {
        await using RunHarness host = await RunHarness.CreateAsync();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<StudioRunReceipt>[] requests = Enumerable.Range(0, 20).Select(_ => Task.Run(async () => { await start.Task; return await host.SubmitAsync("retry"); })).ToArray();
        start.SetResult();
        StudioRunReceipt[] receipts = await Task.WhenAll(requests);
        Assert.Single(receipts.Select(receipt => receipt.RunId).Distinct());
        TestExecution job = await host.NextAsync();
        await Assert.ThrowsAsync<StudioRequestConflictException>(() => host.Service.StartAsync(RunHarness.Request("retry") with { Input = JsonSerializer.SerializeToElement("different") }, RunHarness.Caller, default).AsTask());
        job.Complete();
        StudioRunSnapshot snapshot = await host.TerminalAsync(receipts[0].RunId);
        Assert.Equal(StudioOutcomeClassification.Unclassified, snapshot.Outcome);
        Assert.True(snapshot.Result!.Value.GetProperty("ok").GetBoolean());
        Assert.Equal(1, host.Count);
        Assert.Contains(job.ScopeId, host.DisposedScopes);
    }

    [Fact]
    public async Task FourWorkersBoundQueueAndProtectIndependentSessionLeases()
    {
        await using RunHarness host = await RunHarness.CreateAsync(options => options with { QueueCapacity = 1 });
        List<TestExecution> jobs = [];
        for (int i = 0; i < 4; i++)
        {
            await host.SubmitAsync($"run-{i}", $"session-{i}");
            jobs.Add(await host.NextAsync());
        }
        StudioRunReceipt queued = await host.SubmitAsync("queued", "lease");
        Assert.Equal(StudioRunStatus.Queued, (await host.Service.GetAsync(queued.RunId, default))!.Status);
        await Assert.ThrowsAsync<StudioSessionBusyException>(() => host.SubmitAsync("overlap", "lease"));
        await Assert.ThrowsAsync<StudioCapacityException>(() => host.SubmitAsync("overflow"));
        Assert.Equal(4, host.Count);
        Assert.Equal(4, jobs.Select(j => j.ScopeId).Distinct().Count());
        jobs[0].Complete();
        TestExecution next = await host.NextAsync();
        Assert.Equal("lease", next.Context.SessionId);
        Assert.Equal(RunHarness.Caller, next.Context.Caller);
    }

    [Fact]
    public async Task AcceptedInputOutlivesRequestDisconnectAndJsonDocument()
    {
        await using RunHarness host = await RunHarness.CreateAsync();
        using CancellationTokenSource disconnect = new();
        using JsonDocument input = JsonDocument.Parse("{\"value\":17}");
        StudioRunReceipt receipt = await host.Service.StartAsync(RunHarness.Request("disconnected") with { Input = input.RootElement }, RunHarness.Caller, disconnect.Token);
        input.Dispose();
        disconnect.Cancel();
        TestExecution job = await host.NextAsync();
        Assert.False(job.Token.IsCancellationRequested);
        Assert.Equal(17, job.Request.Input.GetProperty("value").GetInt32());
        job.Complete();
        Assert.Equal(StudioRunStatus.Completed, (await host.TerminalAsync(receipt.RunId)).Status);
    }
}
