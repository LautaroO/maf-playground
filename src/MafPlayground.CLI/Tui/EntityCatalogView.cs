using Terminal.Gui;

namespace MafPlayground.CLI.Tui;

internal sealed record CatalogEntry(string Id, string Kind, string Description);

internal sealed class EntityCatalogView : Window
{
    private readonly Label _hint;
    private const string Keys = "↑↓ select   Enter open   q quit";
    private readonly ListView _list;
    private readonly CatalogEntry[] _entries;

    public EntityCatalogView(string scope, CatalogEntry[] entries, Action<string> open) : base($"MAF Studio · {scope} catalog")
    {
        ColorScheme = StudioPalette.Create();
        _entries = entries;
        _list = new ListView(entries.Select(d => $"{d.Kind,-10} {d.Id}").ToArray())
        { X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(4) };
        var description = new Label(entries[0].Description) { X = 1, Y = Pos.AnchorEnd(3), Width = Dim.Fill(1), Height = 2 };
        _hint = new Label(Keys) { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1) };
        _list.SelectedItemChanged += _ => description.Text = entries[_list.SelectedItem].Description;
        _list.OpenSelectedItem += _ => open(entries[_list.SelectedItem].Id);
        _list.KeyPress += e =>
        {
            if (e.KeyEvent.Key != (Key)'q') return;
            e.Handled = true;
            Application.RequestStop();
        };
        Add(_list, description, _hint);
    }

    public void Select(string id) => _list.SelectedItem = Math.Max(0, Array.FindIndex(_entries, item => item.Id == id));
    public void SetMessage(string message) => _hint.Text = message.Length == 0 ? Keys : message;
    public void FocusList() => _list.SetFocus();
}
