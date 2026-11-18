using System.Text.Json;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using Xunit;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioRetentionTests
{
    [Fact]
    public async Task EvictionPreservesRetryTombstonesAndRejectsMetadataExhaustion()
    {
        await using RunHarness host = await RunHarness.CreateAsync(options => options with { MaxCompletedRuns = 1, MaxRequestRecords = 2 });
        StudioRunReceipt first = await host.SubmitAsync("first");
        (await host.NextAsync()).Complete();
        await host.TerminalAsync(first.RunId);
        StudioRunReceipt second = await host.SubmitAsync("second");
        (await host.NextAsync()).Complete();
        await host.TerminalAsync(second.RunId);
        Assert.Null(await host.Service.GetAsync(first.RunId, default));
        Assert.Equal(first, await host.SubmitAsync("first"));
        await Assert.ThrowsAsync<StudioCapacityException>(() => host.SubmitAsync("third"));
        Assert.Equal(2, host.Count);
    }

    [Fact]
    public async Task SlowSubscriberGetsExplicitGapWithoutBlockingExecution()
    {
        await using RunHarness host = await RunHarness.CreateAsync(options => options with { MaxEventsPerRun = 3 });
        StudioRunReceipt receipt = await host.SubmitAsync("events");
        TestExecution job = await host.NextAsync();
        await using IAsyncEnumerator<StudioEvent> watch = host.Service.WatchAsync(receipt.RunId, 0, default).GetAsyncEnumerator();
        Assert.True(await watch.MoveNextAsync());
        for (int i = 0; i < 12; i++) host.Record(receipt.RunId, new { index = i });
        job.Complete();
        StudioRunSnapshot snapshot = await host.TerminalAsync(receipt.RunId);
        StudioEventPage page = await host.Service.ReadEventsAsync(receipt.RunId, 0, 100, default);
        Assert.True(page.HasGap);
        Assert.Equal(3, page.Events.Count);
        Assert.False(snapshot.CaptureComplete);
        Assert.Equal(page.Events.Select(e => e.Sequence).Order(), page.Events.Select(e => e.Sequence));
        Assert.True(await watch.MoveNextAsync());
        Assert.Equal("journal_gap", watch.Current.Kind);
    }

    [Fact]
    public async Task CaptureAndPayloadPressurePreserveActiveControlAndMarkTruncation()
    {
        await using RunHarness host = await RunHarness.CreateAsync(options => options with { MaxCaptureBytes = 2048, MaxPayloadBytes = 1024, MaxEventsPerRun = 4 });
        StudioRunReceipt receipt = await host.SubmitAsync("pressure", "lease");
        TestExecution job = await host.NextAsync();
        for (int i = 0; i < 20; i++) host.Record(receipt.RunId, new { text = new string('x', 4000) });
        Assert.True(await host.Service.CancelAsync(receipt.RunId, default));
        await Assert.ThrowsAsync<StudioSessionBusyException>(() => host.SubmitAsync("overlap", "lease"));
        Assert.True(job.Token.IsCancellationRequested);
        job.Complete(JsonSerializer.SerializeToElement(new string('x', 8000)));
        StudioRunSnapshot snapshot = await host.TerminalAsync(receipt.RunId);
        Assert.False(snapshot.CaptureComplete);
        Assert.True(snapshot.CapturedBytes <= 2048);
        Assert.Equal("truncated", snapshot.Result!.Value.GetProperty("captureState").GetString());
    }

    [Fact]
    public async Task MetadataByteBudgetRejectsOversizedIdentityBeforeAcceptance()
    {
        await using RunHarness host = await RunHarness.CreateAsync(options => options with { MaxMetadataBytes = 1024 });
        await Assert.ThrowsAsync<StudioCapacityException>(() => host.SubmitAsync(new string('r', 2000)));
        Assert.Equal(0, host.Count);
    }

    [Fact]
    public async Task DefaultCompletedCountEvictsOnlyTheOldestAndPreservesTombstone()
    {
        await using RunHarness host = await RunHarness.CreateAsync();
        List<StudioRunReceipt> receipts = [];
        for (int i = 0; i < 101; i++)
        {
            StudioRunReceipt receipt = await host.SubmitAsync($"completed-{i}");
            receipts.Add(receipt);
            (await host.NextAsync()).Complete();
            await host.TerminalAsync(receipt.RunId);
        }
        Assert.Null(await host.Service.GetAsync(receipts[0].RunId, default));
        foreach (StudioRunReceipt receipt in receipts.Skip(1)) Assert.NotNull(await host.Service.GetAsync(receipt.RunId, default));
        Assert.Equal(receipts[0], await host.SubmitAsync("completed-0"));
        Assert.Equal(101, host.Count);
    }

    [Fact]
    public async Task CaptureBudgetEvictsCompletedBeforeTouchingActiveOwnership()
    {
        await using RunHarness host = await RunHarness.CreateAsync(options => options with { MaxCaptureBytes = 3000, MaxPayloadBytes = 2048 });
        StudioRunReceipt first = await host.SubmitAsync("old");
        (await host.NextAsync()).Complete(JsonSerializer.SerializeToElement(new string('x', 1500)));
        await host.TerminalAsync(first.RunId);
        StudioRunReceipt active = await host.SubmitAsync("active", "lease");
        TestExecution job = await host.NextAsync();
        host.Record(active.RunId, new { text = new string('x', 1500) });
        Assert.Null(await host.Service.GetAsync(first.RunId, default));
        Assert.True(await host.Service.CancelAsync(active.RunId, default));
        Assert.True(job.Token.IsCancellationRequested);
        job.Cancel();
        await host.TerminalAsync(active.RunId);
    }

    [Fact]
    public async Task LateCorrelatedEventsRemainInspectableAfterCompletion()
    {
        await using RunHarness host = await RunHarness.CreateAsync();
        StudioRunReceipt receipt = await host.SubmitAsync("late");
        (await host.NextAsync()).Complete();
        await host.TerminalAsync(receipt.RunId);
        StudioRunSnapshot before = (await host.Service.GetAsync(receipt.RunId, default))!;
        host.Record(receipt.RunId, new { lateSpan = true });
        StudioEventPage page = await host.Service.ReadEventsAsync(receipt.RunId, before.LatestSequence, 10, default);
        Assert.True(Assert.Single(page.Events).Data.GetProperty("lateSpan").GetBoolean());
        Assert.Equal(StudioRunStatus.Completed, (await host.Service.GetAsync(receipt.RunId, default))!.Status);
    }

    [Fact]
    public async Task EventCursorNeverRegressesAndCancelledWatcherDoesNotAffectRun()
    {
        await using RunHarness host = await RunHarness.CreateAsync();
        StudioRunReceipt receipt = await host.SubmitAsync("cursor");
        TestExecution job = await host.NextAsync();
        StudioEventPage page = await host.Service.ReadEventsAsync(receipt.RunId, 100, 10, default);
        Assert.Equal(100, page.NextCursor);
        using CancellationTokenSource cancellation = new();
        await using IAsyncEnumerator<StudioEvent> watch = host.Service.WatchAsync(receipt.RunId, 100, cancellation.Token).GetAsyncEnumerator();
        Task<bool> pending = watch.MoveNextAsync().AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(job.Token.IsCancellationRequested);
        job.Complete();
        Assert.Equal(StudioRunStatus.Completed, (await host.TerminalAsync(receipt.RunId)).Status);
    }
}
