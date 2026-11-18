using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

internal sealed class StudioTerminal : IDisposable
{
    private static StudioTerminal? _current;
    private readonly IApplication _app = Application.Create();
    private readonly CancellationTokenSource _stop;
    private readonly Window _root;
    private Task? _cancellation;

    public static IApplication App => Current._app;
    public static Window Root => Current._root;
    private static StudioTerminal Current => _current ?? throw new InvalidOperationException("No active Studio terminal.");

    public StudioTerminal(CancellationTokenSource stop, string? driver = null)
    {
        _stop = stop;
        try
        {
            _app.Init(driver);
            _root = new Window { BorderStyle = LineStyle.None };
            _root.SetScheme(StudioPalette.Create());
            _app.Keyboard.KeyDown += OnKeyDown;
            // A nested dialog has its own run loop. Unwind each loop on its UI thread.
            _app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
            {
                if (_stop.IsCancellationRequested) _app.RequestStop();
                return true;
            });
            _current = this;
        }
        catch
        {
            _app.Dispose();
            throw;
        }
    }

    private void OnKeyDown(object? sender, Key key)
    {
        if (key != Key.C.WithCtrl) return;
        key.Handled = true;
        _cancellation ??= _stop.CancelAsync();
        _app.RequestStop();
    }

    public Task CancelAsync() => _cancellation ??= _stop.CancelAsync();

    public static void RunRoot() => App.Run(Root);

    public void Dispose()
    {
        _app.Keyboard.KeyDown -= OnKeyDown;
        _current = null;
        _root.Dispose();
        _app.Dispose();
    }
}
