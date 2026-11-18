using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

// Terminal.Gui 2.5 ships TextView; its suggested replacement is a separate package.
#pragma warning disable CS0618
internal sealed class StudioTextView : TextView
#pragma warning restore CS0618
{
    public StudioTextView()
    {
        // The editor can update its document on bracketed paste without repainting it.
        Pasted += (_, _) => SetNeedsDraw();
    }
}
