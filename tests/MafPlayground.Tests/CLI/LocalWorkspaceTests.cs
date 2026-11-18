using MafPlayground.CLI.Inspection;
using MafPlayground.CLI.Tui;
using Microsoft.Extensions.Configuration;
using Terminal.Gui;

namespace MafPlayground.Tests;

public sealed class LocalWorkspaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogRestoresInspectorAndAppliesStartupInputOnlyOnce(bool direct)
    {
        Application.Init(new FakeDriver());
        try
        {
            List<(LocalEntityDescriptor Entity, string? Input, InspectorScreen Screen)> opened = [];
            var workspace = new LocalWorkspace((entity, input) =>
            {
                var screen = new InspectorScreen(entity, input, new ConfigurationBuilder().Build(), null,
                    (_, _, _, _, _) => Task.CompletedTask, CancellationToken.None);
                opened.Add((entity, input, screen));
                return screen;
            }, "\"startup\"");
            workspace.Start(direct ? LocalEntityCatalog.All[1] : null);
            var catalog = Assert.Single(Application.Top.Subviews.OfType<EntityCatalogView>());
            var list = Assert.Single(catalog.Subviews.SelectMany(container => container.Subviews).OfType<ListView>());
            Assert.Equal(Color.Black, catalog.ColorScheme.Normal.Background);
            Assert.Equal(Color.Black, list.ColorScheme.Normal.Background);
            Assert.Equal(Color.BrightCyan, list.ColorScheme.Focus.Background);
            Assert.Equal(LocalEntityCatalog.All.Count, list.Source.Count);
            if (!direct)
            {
                Assert.True(catalog.Visible);
                Assert.Empty(opened);
                list.SelectedItem = 1;
                list.ProcessKey(new KeyEvent(Key.Enter, new KeyModifiers()));
            }
            var first = Assert.Single(opened);
            Assert.Equal(LocalEntityCatalog.All[1], first.Entity);
            Assert.Equal("\"startup\"", first.Input);
            first.Screen.ProcessKey(new KeyEvent(Key.Esc, new KeyModifiers()));
            Assert.True(catalog.Visible);
            list.SelectedItem = 0;
            list.ProcessKey(new KeyEvent(Key.Enter, new KeyModifiers()));
            Assert.Null(opened[1].Input);
            opened[1].Screen.ProcessKey(new KeyEvent(Key.Esc, new KeyModifiers()));
            list.SelectedItem = 1;
            list.ProcessKey(new KeyEvent(Key.Enter, new KeyModifiers()));
            Assert.Equal(2, opened.Count);
            Assert.True(first.Screen.Visible);
            Assert.False(opened[1].Screen.Visible);
            await workspace.FinishAsync();
        }
        finally { Application.Shutdown(); }
    }
}
