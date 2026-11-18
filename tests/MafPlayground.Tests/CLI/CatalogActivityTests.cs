using System.Text.Json;
using MafPlayground.CLI.Inspection;
using MafPlayground.CLI.Tui;
using Microsoft.Extensions.Configuration;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace MafPlayground.Tests;

public sealed class CatalogActivityTests
{
    [Fact]
    public void StudioHistoryIsScopedAndUsesExecutionDuration()
    {
        var now = DateTimeOffset.Parse("2026-10-01T10:11:20Z");
        object Snapshot(string runId, int status, DateTimeOffset? start, DateTimeOffset? end, string host = "one", string profile = "test") =>
            new
            {
                hostInstanceId = host,
                runId,
                request = new { entityId = "worker", profileId = profile },
                status,
                submittedAt = now.AddHours(-1),
                startedAt = start,
                completedAt = end,
                totalEvents = 9
            };
        var json = JsonSerializer.SerializeToElement(new[]
        {
            Snapshot("complete", 2, now.AddMinutes(-2), now.AddMinutes(-1)),
            Snapshot("live", 1, now.AddSeconds(-12), null),
            Snapshot("queued", 0, null, null),
            Snapshot("other host", 2, now, now, host: "two"),
            Snapshot("other profile", 2, now, now, profile: "other")
        });
        (string, string)? opened = null;
        var runs = CatalogRun.FromStudio(json, "one", "test", now, (e, r) => opened = (e, r)).ToArray();
        Assert.Equal(3, runs.Length);
        Assert.Equal(TimeSpan.FromMinutes(1), runs[0].Duration);
        Assert.Equal(9, runs[0].Events);
        Assert.False(runs[0].Active);
        Assert.Equal(TimeSpan.FromSeconds(12), runs[1].Duration);
        Assert.True(runs[1].Active);
        Assert.Null(runs[2].Duration);
        runs[1].Open();
        Assert.Equal(("worker", "live"), opened);
    }

    [Fact]
    public async Task ReturningToCatalogShowsAndReopensAnExecutingLocalRun()
    {
        using var stop = new CancellationTokenSource();
        using var terminal = new StudioTerminal(stop, "ansi");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = new LocalWorkspace((entity, input) => new InspectorScreen(entity, input,
            new ConfigurationBuilder().Build(), "ollama:test", async (_, _, _, publish, token) =>
            {
                await release.Task.WaitAsync(token);
                publish(run => run.Complete("done"));
            }, CancellationToken.None), null);
        workspace.Start(LocalEntityCatalog.All[0]);
        var state = StudioTerminal.App.Begin(StudioTerminal.Root);
        try
        {
            StudioTerminal.App.LayoutAndDraw(true);
            var inspector = Assert.Single(StudioTerminal.Root.SubViews.OfType<InspectorScreen>());
            var catalog = Assert.Single(StudioTerminal.Root.SubViews.OfType<EntityCatalogView>());
            inspector.NewKeyDownEvent((Key)'r');
            inspector.NewKeyDownEvent(Key.Esc);
            var detail = Assert.Single(catalog.SubViews.OfType<CatalogDetail>());
            var run = Assert.Single(detail.History);
            Assert.True(run.Active);
            Assert.NotNull(run.Duration);
            Assert.False(inspector.Visible);
            StudioTerminal.Root.NewKeyDownEvent((Key)'l');
            Assert.True(inspector.Visible);
            Assert.False(catalog.Visible);
        }
        finally
        {
            StudioTerminal.App.End(state!);
            release.TrySetResult();
            await workspace.FinishAsync();
        }
    }

    [Fact]
    public void ActivityTabsRetainTheExactRunAcrossRefreshAndOfferAFirstExample()
    {
        using var stop = new CancellationTokenSource();
        using var terminal = new StudioTerminal(stop, "ansi");
        string? opened = null;
        var catalog = new EntityCatalogView("test", [new("unavailable", "Agent", "", false, "{}"),
            new("alpha", "Agent", "", InputExample: "{}"), new("beta", "Workflow", "")], id => opened = id);
        StudioTerminal.Root.Add(catalog);
        var state = StudioTerminal.App.Begin(StudioTerminal.Root);
        try
        {
            StudioTerminal.App.LayoutAndDraw(true);
            catalog.FocusList();
            void Press(Key key) => StudioTerminal.Root.NewKeyDownEvent(key);
            Press((Key)'n');
            Assert.Equal("alpha", opened);
            var now = DateTimeOffset.UtcNow;
            CatalogRun Run(string id, string entity, string status, int offset) =>
                new(entity, now.AddSeconds(offset), status, null, 0, () => opened = id, id);
            var failed = Run("first-failure", "beta", "Partial result", 1);
            var timeout = Run("timeout", "alpha", "Timed out", 2);
            var live = Run("live", "beta", "Queued", 3);
            var done = Run("done", "alpha", "Completed", 4);
            catalog.SetActivity([done, live, timeout, failed]);
            Assert.DoesNotContain(catalog.Actions(), a => a.Shortcut == "n");
            Press((Key)'2');
            Press(Key.Enter);
            Assert.Equal("live", opened);
            Press((Key)'3');
            Press(Key.CursorDown);
            catalog.SetActivity([Run("new-failure", "alpha", "Failed", 5), done, live, timeout, failed]);
            Press(Key.Enter);
            Assert.Equal("first-failure", opened);
            Press((Key)'1');
            Press(Key.Enter);
            Assert.Equal("unavailable", opened);
        }
        finally { StudioTerminal.App.End(state!); }
    }

    [Theory]
    [InlineData(72, 23, false)]
    [InlineData(110, 26, true)]
    public void CatalogRendersMeasuredBarsAndKeepsSelection(int width, int height, bool wide)
    {
        using var stop = new CancellationTokenSource();
        using var terminal = new StudioTerminal(stop, "ansi");
        var driver = StudioTerminal.App.Driver!;
        driver.SetScreenSize(width, height);
        StudioTerminal.Root.Frame = new(0, 0, width, height);
        var catalog = new EntityCatalogView("local",
            [new("alpha", "Agent", "Assistant"), new("beta", "Workflow", "Workflow description")], _ => { });
        StudioTerminal.Root.Add(catalog);
        var state = StudioTerminal.App.Begin(StudioTerminal.Root);
        try
        {
            StudioTerminal.App.LayoutAndDraw(true);
            catalog.Select("beta");
            var views = catalog.SubViews.ToArray();
            var detail = Assert.Single(views.OfType<CatalogDetail>());
            var list = Assert.Single(views.OfType<ListView>());
            Assert.Equal(wide, detail.Frame.X > list.Frame.X);
            Assert.Contains("No runs yet.", Render());
            var now = DateTimeOffset.UtcNow;
            const string newest = "newest";
            string? opened = null;
            catalog.SetActivity([
                new("beta", now.AddMinutes(-1), "Completed", TimeSpan.FromSeconds(1), 3, () => opened = "old"),
                new("alpha", now, "Running", TimeSpan.FromSeconds(3), 7, () => { }),
                new("beta", now, "Failed", TimeSpan.FromSeconds(2), 11, () => opened = newest)
            ]);
            Assert.Equal(1, list.SelectedItem);
            var screen = Render();
            Assert.Contains("3 runs   1 active", screen);
            Assert.Contains("Run coverage", screen);
            Assert.Contains("2/2 entities", screen);
            Assert.Contains($"{2.0:0.0}s", screen);
            Assert.Contains($"{1.0:0.0}s", screen);
            Assert.Contains("━━", screen);
            Assert.Contains("Latest · 11 events", screen);
            catalog.FocusList();
            StudioTerminal.Root.NewKeyDownEvent((Key)'l');
            Assert.Equal(newest, opened);

            string Render()
            {
                StudioTerminal.App.LayoutAndDraw(true);
                var cells = driver.Contents!;
                return string.Join('\n', Enumerable.Range(0, cells.GetLength(0)).Select(y =>
                    string.Concat(Enumerable.Range(0, cells.GetLength(1)).Select(x => cells[y, x].Grapheme))));
            }
        }
        finally { StudioTerminal.App.End(state!); }
    }
}
