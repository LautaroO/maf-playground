using System.Globalization;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

internal sealed record StudioAction(string Title, string Shortcut, Action Execute, string Keywords = "", Func<string?>? Unavailable = null)
{
    public void Invoke()
    {
        if (Unavailable?.Invoke() is { } reason) MessageBox.Query(StudioTerminal.App, Title, reason, "Close");
        else Execute();
    }
}

internal static class ActionPicker
{
    public static bool IsShortcut(Key key) => key == Key.P.WithCtrl || key == ':';

    // ListView and TextView also bind control keys. Give the workspace shortcut
    // first refusal at the root, while leaving nested editors and dialogs alone.
    public static EventHandler<Key> AtRoot(Func<IEnumerable<StudioAction>> available) => (_, key) =>
    {
        if (key.Handled || StudioTerminal.App.TopRunnableView != StudioTerminal.Root) return;
        if (StudioTerminal.Root.SubViews.OfType<PaletteWindow>().FirstOrDefault() is { } palette)
            key.Handled = palette.HandleShortcut(key);
        else if (IsShortcut(key))
        {
            Show(available());
            key.Handled = true;
        }
    };

    public static bool Dispatch(StudioAction[] actions, Key key)
    {
        if (IsShortcut(key)) { Show(actions); return true; }
        if (!key.AsRune.IsAscii || key.AsRune.Value == 0) return false;
        var command = Array.Find(actions, a => a.Shortcut.Length == 1 && a.Shortcut[0] == (char)key.AsRune.Value);
        if (command is null) return false;
        command.Invoke();
        return true;
    }

    public static StudioAction[] Filter(IEnumerable<StudioAction> actions, string query)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return actions.Where(action => words.All(word => CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            $"{action.Title} {action.Shortcut} {action.Keywords}", word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)).ToArray();
    }

    public static void Show(IEnumerable<StudioAction> actions)
    {
        var window = new PaletteWindow(actions.ToArray());
        StudioTerminal.Root.Add(window);
        window.SetFocus();
    }

    private sealed class PaletteWindow : Window
    {
        private readonly View? _previousFocus = StudioTerminal.Root.MostFocused;
        private readonly View[] _siblings = StudioTerminal.Root.SubViews.Where(view => view.Enabled).ToArray();
        private readonly TextField _query = new() { Width = Dim.Fill() };

        public PaletteWindow(StudioAction[] all)
        {
            Title = "Find an action";
            X = Pos.Center();
            Y = Pos.Center();
            Width = Dim.Percent(80);
            Height = Dim.Percent(68);
            SetScheme(StudioPalette.Create());
            foreach (var sibling in _siblings) sibling.Enabled = false;
            var matches = all;
            var list = new ListView { Y = 2, Width = Dim.Fill(), Height = Dim.Fill(1), CanFocus = false };
            var hint = new Label { Y = Pos.AnchorEnd(1), Width = Dim.Fill() };
            void Update()
            {
                matches = Filter(all, _query.Text);
                list.SetSource<string>([.. matches.Select(action =>
                {
                    var label = action.Shortcut.Length == 0 ? action.Title : $"{action.Title,-32}  {action.Shortcut}";
                    return action.Unavailable?.Invoke() is { } reason ? $"{label}  ·  {reason}" : label;
                })]);
                if (matches.Length > 0) list.Value = 0;
                hint.Text = matches.Length == 0 ? "No matches   Esc back   Ctrl+C quit" : "↑↓ select   Enter run   Esc back   Ctrl+C quit   q quit when empty";
            }
            void Choose()
            {
                if (list.Value is not int index || index < 0 || index >= matches.Length) return;
                var chosen = matches[index];
                if (chosen.Unavailable?.Invoke() is { } reason) { hint.Text = reason + "   ·   Esc back"; return; }
                Close();
                chosen.Execute();
            }
            _query.TextChanged += (_, _) => Update();
            _query.KeyDown += (_, key) =>
            {
                if (key == Key.CursorDown) list.MoveDown();
                else if (key == Key.CursorUp) list.MoveUp();
                else if (key == Key.Enter) Choose();
                else return;
                key.Handled = true;
            };
            list.Accepting += (_, e) => { e.Handled = true; Choose(); };
            Add(_query, list, hint);
            Update();
        }

        public bool HandleShortcut(Key key)
        {
            if (key == 'q' && _query.Text.Length == 0)
            {
                StudioTerminal.App.RequestStop();
                return true;
            }
            if (key != Key.Esc && key != Key.P.WithCtrl) return false;
            Close();
            return true;
        }

        private void Close()
        {
            StudioTerminal.Root.Remove(this);
            foreach (var sibling in _siblings) sibling.Enabled = true;
            _previousFocus?.SetFocus();
            Dispose();
        }
    }
}
