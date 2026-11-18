using MafPlayground.CLI.Inspection;
using MafPlayground.CLI.Tui;
using Microsoft.Extensions.Configuration;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Terminal.Gui.Drawing;

namespace MafPlayground.Tests;

public sealed class LocalWorkspaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogRestoresInspectorAndAppliesStartupInputOnlyOnce(bool direct)
    {
        using var stop = new CancellationTokenSource();
        using var terminal = new StudioTerminal(stop, "ansi");
        List<(LocalEntityDescriptor Entity, string? Input, InspectorScreen Screen)> opened = [];
        var workspace = new LocalWorkspace((entity, input) =>
        {
            var screen = new InspectorScreen(entity, input, new ConfigurationBuilder().Build(), null,
                (_, _, _, _, _) => Task.CompletedTask, CancellationToken.None);
            opened.Add((entity, input, screen));
            return screen;
        }, "\"startup\"");
        workspace.Start(direct ? LocalEntityCatalog.All[1] : null);
        var catalog = Assert.Single(StudioTerminal.Root.SubViews.OfType<EntityCatalogView>());
        var list = Assert.Single(catalog.SubViews.OfType<ListView>());
        Assert.Equal(Color.Black, catalog.GetScheme().Normal.Background);
        Assert.Equal(Color.Black, list.GetScheme().Normal.Background);
        Assert.Equal(Color.BrightCyan, list.GetScheme().Focus.Background);
        Assert.Equal(LocalEntityCatalog.All.Count, list.Source!.Count);
        if (!direct)
        {
            Assert.True(catalog.Visible);
            Assert.Empty(opened);
            list.SelectedItem = 1;
            list.NewKeyDownEvent(Key.Enter);
        }
        var first = Assert.Single(opened);
        Assert.Equal(LocalEntityCatalog.All[1], first.Entity);
        Assert.Equal("\"startup\"", first.Input);
        first.Screen.NewKeyDownEvent(Key.Esc);
        Assert.True(catalog.Visible);
        list.SelectedItem = 0;
        list.NewKeyDownEvent(Key.Enter);
        Assert.Null(opened[1].Input);
        opened[1].Screen.NewKeyDownEvent(Key.Esc);
        list.SelectedItem = 1;
        list.NewKeyDownEvent(Key.Enter);
        Assert.Equal(2, opened.Count);
        Assert.True(first.Screen.Visible);
        Assert.False(opened[1].Screen.Visible);
        await workspace.FinishAsync();
    }
}
