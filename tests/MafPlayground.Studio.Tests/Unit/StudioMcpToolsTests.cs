using System.Text.Json;
using MafPlayground.AI.Abstractions;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using MafPlayground.Studio.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioMcpToolsTests
{
    [Fact]
    public async Task Accepted_cancellation_returns_receipt_when_snapshot_is_evicted_during_cancellation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStudioRunService, EvictingRunService>();
        await using var provider = services.BuildServiceProvider();
        var tools = new StudioMcpTools(provider.GetRequiredService<IServiceScopeFactory>());
        string result = await tools.CancelRun("run", default);
        var receipt = JsonSerializer.Deserialize<StudioRunReceipt>(result, JsonSerializerOptions.Web);
        Assert.Equal(new StudioRunReceipt("host", "run"), receipt);
        Assert.Null(await provider.GetRequiredService<IStudioRunService>().GetAsync("run", default));
    }

    [Fact]
    public async Task Span_projection_preserves_an_existing_journal_gap_and_cursor_metadata()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStudioRunService, EvictingRunService>();
        await using var provider = services.BuildServiceProvider();
        var tools = new StudioMcpTools(provider.GetRequiredService<IServiceScopeFactory>());
        string result = await tools.GetSpans("run", default);
        var page = JsonSerializer.Deserialize<StudioMcpSpanPage>(result, JsonSerializerOptions.Web)!;
        Assert.Empty(page.Spans);
        Assert.True(page.HasGap);
        Assert.Equal(44, page.NextCursor);
        Assert.Equal(42, page.EarliestRetainedSequence);
    }

    private sealed class EvictingRunService : IStudioRunService
    {
        private StudioRunSnapshot? snapshot = new("host", "run",
            new("request", "entity", "cli-default", JsonSerializer.SerializeToElement(new { }), null),
            StudioRunStatus.Running, false, DateTimeOffset.UtcNow, null, null, null,
            StudioOutcomeClassification.Unclassified, null, null, 0, 0, true, 0, 0);

        public ValueTask<StudioRunSnapshot?> GetAsync(string runId, CancellationToken cancellationToken) => ValueTask.FromResult(snapshot);
        public ValueTask<bool> CancelAsync(string runId, CancellationToken cancellationToken)
        {
            snapshot = null;
            return ValueTask.FromResult(true);
        }
        public ValueTask<StudioRunReceipt> StartAsync(StudioRunRequest request, AICallerContext caller, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<StudioRunSnapshot>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<StudioEventPage> ReadEventsAsync(string runId, long after, int limit, CancellationToken cancellationToken) => ValueTask.FromResult(new StudioEventPage([], 44, 42, true));
        public IAsyncEnumerable<StudioEvent> WatchAsync(string runId, long after, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
