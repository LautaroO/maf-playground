using MafPlayground.CLI.Inspection;
using Terminal.Gui;

namespace MafPlayground.CLI.Tui;

internal sealed class LocalWorkspace(Func<LocalEntityDescriptor, string?, InspectorScreen> create, string? initialInput)
{
    private readonly Dictionary<string, InspectorScreen> _screens = [];
    private EntityCatalogView _catalog = null!;
    private InspectorScreen? _current;
    private string? _firstInput = initialInput;

    public void Start(LocalEntityDescriptor? initial)
    {
        _catalog = new EntityCatalogView("Local", LocalEntityCatalog.All.Select(d =>
            new CatalogEntry(d.Id, d.Kind.ToString(), d.Description)).ToArray(), id =>
            Open(LocalEntityCatalog.All.First(d => d.Id == id)));
        Application.Top.Add(_catalog);
        if (initial is null) ShowCatalog();
        else Open(initial);
    }

    private void ShowCatalog()
    {
        if (_current is not null) _current.Visible = false;
        _catalog.Visible = true;
        _catalog.FocusList();
    }

    private void Open(LocalEntityDescriptor entity)
    {
        if (!_screens.TryGetValue(entity.Id, out var screen))
        {
            screen = create(entity, _firstInput);
            _firstInput = null;
            screen.CatalogRequested += ShowCatalog;
            _screens.Add(entity.Id, screen);
            Application.Top.Add(screen);
            screen.Start();
        }
        _catalog.Select(entity.Id);
        _catalog.Visible = false;
        _current = screen;
        screen.Visible = true;
        screen.SetFocus();
    }

    public Task FinishAsync() => Task.WhenAll(_screens.Values.Select(screen => screen.FinishAsync()));
}
