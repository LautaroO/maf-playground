using System.Runtime.ExceptionServices;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

internal static class StudioFileDialog
{
    // Storage may reside on a mounted drive. Keep the modal event loop pumping
    // while the worker accesses it. Only the UI thread ever touches the dialog.
    public static T Run<T>(string title, Func<T> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var dialog = new Dialog { Title = title, Width = 48, Height = 6 };
        dialog.Add(new Label { Text = "Accessing files…" });
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        Task task = Task.Run(() =>
        {
            try { result = operation(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        }, token);
        dialog.IsRunningChanging += (_, e) => e.Cancel = !e.NewValue && !task.IsCompleted && !token.IsCancellationRequested;
        var timer = StudioTerminal.App.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            if (StudioTerminal.App.TopRunnable != dialog || (!task.IsCompleted && !token.IsCancellationRequested)) return true;
            StudioTerminal.App.RequestStop();
            return false;
        });
        try { StudioTerminal.App.Run(dialog); }
        finally { if (timer is not null) StudioTerminal.App.RemoveTimeout(timer); }
        token.ThrowIfCancellationRequested();
        failure?.Throw();
        return result;
    }

    public static void Run(string title, Action operation, CancellationToken token) =>
        Run(title, () => { operation(); return true; }, token);
}
