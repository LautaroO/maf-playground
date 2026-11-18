using MafPlayground.CLI.Tui;

namespace MafPlayground.Tests;

public sealed class StudioFileDialogTests
{
    [Fact]
    public void ProcessesUiEventsDuringFileAccessAndPropagatesErrors()
    {
        using var stop = new CancellationTokenSource();
        using var terminal = new StudioTerminal(stop, "ansi");
        using var release = new ManualResetEventSlim(false);
        var state = StudioTerminal.App.Begin(StudioTerminal.Root);
        try
        {
            int uiThread = Environment.CurrentManagedThreadId;
            StudioTerminal.App.AddTimeout(TimeSpan.FromMilliseconds(10), () => { release.Set(); return false; });
            int worker = StudioFileDialog.Run("Read cases", () =>
            {
                Assert.True(release.Wait(TimeSpan.FromSeconds(3)));
                return Environment.CurrentManagedThreadId;
            }, CancellationToken.None);
            Assert.NotEqual(uiThread, worker);
            Assert.Throws<IOException>(() => StudioFileDialog.Run("Open capture", () => throw new IOException("Read failed"), CancellationToken.None));
        }
        finally { StudioTerminal.App.End(state!); }
    }
}
