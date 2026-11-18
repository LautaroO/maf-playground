using MafPlayground.CLI.Inspection;

namespace MafPlayground.CLI.Tui;

internal sealed class LocalWorkspace(Func<LocalEntityDescriptor, string?, InspectorScreen> create, string? initialInput)
{
    private readonly Dictionary<string, InspectorScreen> _screens = [];
    private EntityCatalogView _catalog = null!;
    private InspectorScreen? _current;
    private string? _firstInput = initialInput;

    public IEnumerable<StudioAction> AvailableActions() =>
        _current is null || _catalog.Visible ? _catalog.Actions() : _current.Commands;

    public void Start(LocalEntityDescriptor? initial)
    {
        _catalog = new EntityCatalogView("Local", LocalEntityCatalog.All.Select(d =>
            new CatalogEntry(d.Id, d.Kind.ToString(), d.Description, InputExample: InspectorRun.Format(d.InputExample))).ToArray(), id =>
            Open(LocalEntityCatalog.All.First(d => d.Id == id)));
        StudioTerminal.Root.Add(_catalog);
        StudioTerminal.App.AddTimeout(TimeSpan.FromMilliseconds(500), () =>
        {
            if (_catalog.Visible) RefreshActivity();
            return true;
        });
        if (initial is null) ShowCatalog();
        else Open(initial);
    }

    private void ShowCatalog()
    {
        if (_current is not null) _current.Visible = false;
        RefreshActivity();
        _catalog.Visible = true;
        _catalog.FocusList();
    }

    private void RefreshActivity() => _catalog.SetActivity(_screens.SelectMany(pair =>
        pair.Value.CatalogRuns(() => Open(LocalEntityCatalog.All.First(d => d.Id == pair.Key)))));

    private void Open(LocalEntityDescriptor entity)
    {
        if (!_screens.TryGetValue(entity.Id, out var screen))
        {
            screen = create(entity, _firstInput);
            _firstInput = null;
            screen.CatalogRequested += ShowCatalog;
            _screens.Add(entity.Id, screen);
            StudioTerminal.Root.Add(screen);
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
