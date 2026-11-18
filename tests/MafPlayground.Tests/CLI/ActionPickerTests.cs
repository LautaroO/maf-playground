using MafPlayground.CLI.Tui;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace MafPlayground.Tests;

public sealed class ActionPickerTests
{
    [Fact]
    public void PaletteRestoresFocusAndRoutesExitKeysWhileSearching()
    {
        using var stop = new CancellationTokenSource();
        using var terminal = new StudioTerminal(stop, "ansi");
        var workspace = new TextField();
        StudioTerminal.Root.Add(workspace);
        StudioTerminal.App.AddTimeout(TimeSpan.FromMilliseconds(10), () =>
        {
            workspace.SetFocus();
            var calls = 0;
            StudioTerminal.App.Keyboard.KeyDown += ActionPicker.AtRoot(() =>
                [new("Request", "", () => calls++), new("Unavailable", "", () => calls++, Unavailable: () => "Needs another capture.")]);
            static bool Press(Key key) => StudioTerminal.App.Keyboard.RaiseKeyDownEvent(key);

            Assert.True(Press(Key.P.WithCtrl));
            Assert.False(workspace.Enabled);
            Assert.True(Press(Key.CursorDown));
            Assert.True(Press(Key.Enter));
            Assert.Equal(0, calls);
            Assert.False(workspace.Enabled);
            Assert.True(Press(Key.Esc));
            Assert.Same(workspace, StudioTerminal.Root.MostFocused);
            Assert.True(Press(Key.P.WithCtrl));
            Assert.True(Press(Key.P.WithCtrl));
            Assert.Same(workspace, StudioTerminal.Root.MostFocused);
            Assert.True(Press(Key.P.WithCtrl));
            foreach (char c in "req") Assert.True(Press((Key)c));
            Assert.True(Press(Key.Enter));
            Assert.Equal(1, calls);
            Assert.Same(workspace, StudioTerminal.Root.MostFocused);
            Assert.True(Press(Key.P.WithCtrl));
            Assert.True(Press((Key)'r'));
            Assert.True(Press(Key.C.WithCtrl));
            Assert.True(stop.IsCancellationRequested);
            return false;
        });
        StudioTerminal.RunRoot();
        Assert.False(StudioTerminal.Root.IsRunning);
    }

    [Fact]
    public void MatchesEverySearchWordAcrossTitleAndAliasesInEitherLanguage()
    {
        StudioAction[] commands =
        [
            new("Compare runs on graph", "b", () => { }, "comparar corridas diferencias"),
            new("View metrics", "", () => { }, "métricas tokens")
        ];
        Assert.Equal(commands, ActionPicker.Filter(commands, ""));
        Assert.Same(commands[0], Assert.Single(ActionPicker.Filter(commands, "CORRIDAS graph")));
        Assert.Same(commands[1], Assert.Single(ActionPicker.Filter(commands, "metricas")));
        Assert.Empty(ActionPicker.Filter(commands, "metrics graph"));
    }

    [Fact]
    public void DispatchExecutesOnlyTheMatchingShortcutAndLeavesNavigationToTheView()
    {
        int calls = 0;
        StudioAction[] commands = [new("Run entity", "r", () => calls++), new("View metrics", "", () => calls++)];
        Assert.True(ActionPicker.Dispatch(commands, (Key)'r'));
        Assert.False(ActionPicker.Dispatch(commands, Key.Enter));
        Assert.False(ActionPicker.Dispatch(commands, Key.CursorDown));
        Assert.Equal(1, calls);
        Assert.True(ActionPicker.IsShortcut(Key.P.WithCtrl));
        Assert.True(ActionPicker.IsShortcut((Key)':'));
    }
}
