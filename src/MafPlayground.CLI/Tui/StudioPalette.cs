using Terminal.Gui;

namespace MafPlayground.CLI.Tui;

internal static class StudioPalette
{
    public static ColorScheme Create() => new()
    {
        Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black),
        Focus = Application.Driver.MakeAttribute(Color.Black, Color.BrightCyan),
        HotNormal = Application.Driver.MakeAttribute(Color.BrightCyan, Color.Black),
        HotFocus = Application.Driver.MakeAttribute(Color.Black, Color.BrightCyan),
        Disabled = Application.Driver.MakeAttribute(Color.DarkGray, Color.Black)
    };
}
