using Terminal.Gui.Drawing;

namespace MafPlayground.CLI.Tui;

internal static class StudioPalette
{
    public static Scheme Create() => new()
    {
        Normal = new(Color.Gray, Color.Black),
        Focus = new(Color.Black, Color.BrightCyan),
        HotNormal = new(Color.BrightCyan, Color.Black),
        HotFocus = new(Color.Black, Color.BrightCyan),
        Disabled = new(Color.DarkGray, Color.Black)
    };
}
