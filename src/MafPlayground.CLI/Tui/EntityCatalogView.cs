using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;

namespace MafPlayground.CLI.Tui;

internal sealed record CatalogEntry(string Id, string Kind, string Description, bool Available = true, string? InputExample = null);

internal sealed class EntityCatalogView : Window
{
    private readonly Label _summary = new() { X = 1, Width = Dim.Fill(1) };
    private readonly Label _coverage = new() { X = 1, Y = 1, Width = Dim.Fill(1) };
    private enum Section { Entities, Running, Failures }
    private Section _section;
    private readonly Label _heading = new() { X = 1, Y = 3 };
    private readonly Button _entities = new() { Text = "Entities", X = 1, Y = 2 };
    private readonly Button _running = new() { Text = "Running now", Y = 2 };
    private readonly Button _failures = new() { Text = "Recent failures", Y = 2 };
    private CatalogRun[] _visibleRuns = [];
    private string? _entitySelection;
    private readonly Label _hint = new() { X = 1, Y = Pos.AnchorEnd(1), Width = Dim.Fill(1) };
    private readonly ListView _list;
    private readonly CatalogDetail _detail = new();
    private readonly CatalogEntry[] _entries;
    private readonly Action<string> _open;
    private CatalogRun[] _runs = [];
    private string[] _rows = [];
    private bool _wide;
    private int _rowWidth = 40;
    private (int Width, int Height) _layoutSize;
    private string _message = "";
    private string? _historyMessage;

    public EntityCatalogView(string scope, CatalogEntry[] entries, Action<string> open)
    {
        Title = $"MAF Studio · {scope} catalog";
        SetScheme(StudioPalette.Create());
        _entries = entries;
        _open = open;
        _list = new CatalogList(CatalogShortcut) { X = 1, Y = 4 };
        _running.X = Pos.Right(_entities);
        _failures.X = Pos.Right(_running);
        _entities.Accepting += (_, e) => { e.Handled = true; ShowSection(Section.Entities); };
        _running.Accepting += (_, e) => { e.Handled = true; ShowSection(Section.Running); };
        _failures.Accepting += (_, e) => { e.Handled = true; ShowSection(Section.Failures); };
        _list.ValueChanged += (_, _) => UpdateDetail();
        _list.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (SelectedRun is { } run) run.Open();
            else if (Selected is { } entry) open(entry.Id);
        };
        Add(_summary, _coverage, _entities, _running, _failures, _heading, _list, _detail, _hint);
        ViewportChanged += (_, _) => Arrange();
        SetActivity([]);
    }

    protected override bool OnKeyDown(Key key) => CatalogShortcut(key) || base.OnKeyDown(key);

    // ListView consumes printable keys for incremental search before raising KeyDown.
    private sealed class CatalogList(Func<Key, bool> shortcut) : ListView
    {
        protected override bool OnKeyDown(Key key) => shortcut(key) || base.OnKeyDown(key);
    }

    private bool CatalogShortcut(Key key)
    {
        if (ActionPicker.IsShortcut(key)) ActionPicker.Show(Actions());
        else if (key == (Key)'1') ShowSection(Section.Entities);
        else if (key == (Key)'2') ShowSection(Section.Running);
        else if (key == (Key)'3') ShowSection(Section.Failures);
        else if (key == (Key)'n' && FirstExample is { } example) _open(example.Id);
        else if (key == (Key)'l' && _section == Section.Entities && _detail.History.FirstOrDefault() is { } latest) latest.Open();
        else if (key == (Key)'q') StudioTerminal.App.RequestStop();
        else return false;
        return true;
    }

    internal IEnumerable<StudioAction> Actions()
    {
        yield return new("Browse entities", "1", () => ShowSection(Section.Entities));
        yield return new("Running now", "2", () => ShowSection(Section.Running));
        yield return new("Recent failures", "3", () => ShowSection(Section.Failures));
        if (FirstExample is { } example)
            yield return new("Open first example · " + example.Id, "n", () => _open(example.Id));
        if (_section == Section.Entities && _detail.History.FirstOrDefault() is { } latest)
            yield return new("Open latest run · " + latest.Entity, "l", latest.Open);
        foreach (var entry in _entries)
            yield return new("Open " + entry.Id, "", () => _open(entry.Id), entry.Kind + " " + entry.Description);
        yield return new("Quit Studio", "q", () => StudioTerminal.App.RequestStop());
    }

    private CatalogEntry? FirstExample => _runs.Length == 0 && _historyMessage is null
        ? _entries.FirstOrDefault(e => e.Available && e.InputExample is { Length: > 0 }) : null;

    private CatalogRun? SelectedRun => _visibleRuns.ElementAtOrDefault(_list.SelectedItem ?? -1);
    private CatalogEntry? Selected => _section == Section.Entities ? _entries.ElementAtOrDefault(_list.SelectedItem ?? -1)
        : _entries.FirstOrDefault(e => e.Id == SelectedRun?.Entity);

    private void ShowSection(Section section)
    {
        if (_section == Section.Entities) _entitySelection = Selected?.Id;
        if (_section != section)
        {
            _section = section;
            UpdateRows();
            if (_rows.Length > 0)
                _list.SelectedItem = section == Section.Entities
                    ? Math.Max(0, Array.FindIndex(_entries, e => e.Id == _entitySelection)) : 0;
            UpdateDetail();
        }
        FocusList();
    }

    private void Arrange()
    {
        int width = Math.Max(1, Viewport.Width), height = Math.Max(1, Viewport.Height);
        if (_layoutSize == (width, height)) return;
        _layoutSize = (width, height);
        _wide = width >= 94;
        int split = _wide ? width / 2 : width;
        int listHeight = _wide ? height - 6 : Math.Clamp(Math.Min(height / 3, _entries.Length), 1, Math.Max(1, height - 9));
        _rowWidth = Math.Max(1, split - 2);
        _list.Width = _rowWidth;
        _list.Height = Math.Max(1, listHeight);
        _detail.X = _wide ? split + 1 : 1;
        _detail.Y = _wide ? 3 : 5 + listHeight;
        _detail.Width = Dim.Fill(1);
        _detail.Height = Dim.Fill(1);
        UpdateRows();
        UpdateHint();
    }

    public void SetActivity(IEnumerable<CatalogRun> runs, string? historyMessage = null)
    {
        var next = runs.OrderByDescending(r => r.Submitted).ToArray();
        bool unchanged = _rows.Length > 0 && _historyMessage == historyMessage && next.Length == _runs.Length &&
            next.Zip(_runs).All(pair => pair.First.SameDisplayAs(pair.Second));
        if (unchanged) return;
        _runs = next;
        _historyMessage = historyMessage;
        static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
        _summary.Text = Count(_entries.Count(e => e.Kind == "Agent"), "agent") + "   " +
            Count(_entries.Count(e => e.Kind == "Workflow"), "workflow") + "   │   " +
            (historyMessage ?? $"{Count(_runs.Length, "run")}   {_runs.Count(r => r.Active)} active");
        _running.Text = $"Running now ({_runs.Count(r => r.Active)})";
        _failures.Text = $"Recent failures ({_runs.Count(r => r.Failed)})";
        int observed = _entries.Count(entry => _runs.Any(run => run.Entity == entry.Id));
        int filled = _entries.Length == 0 ? 0 : (int)Math.Round(12d * observed / _entries.Length);
        string coverage = new string('━', filled) + new string('·', 12 - filled);
        _coverage.Text = historyMessage is null
            ? $"Run coverage  {coverage}  {observed}/{_entries.Length} entities"
            : "";
        UpdateRows();
        UpdateDetail();
    }

    public void SetHistoryMessage(string message) => SetActivity(_runs, message);

    private void UpdateRows()
    {
        int width = _rowWidth;
        int nameWidth = Math.Clamp(width - 22, 9, 35);
        var previous = SelectedRun;
        _visibleRuns = _section switch
        {
            Section.Running => _runs.Where(r => r.Active).ToArray(),
            Section.Failures => _runs.Where(r => r.Failed).ToArray(),
            _ => []
        };
        _heading.Text = _section switch
        {
            Section.Running => $"RUNNING NOW  ·  {_visibleRuns.Length}",
            Section.Failures => $"RECENT FAILURES  ·  {_visibleRuns.Length}",
            _ => "ENTITIES"
        };
        string[] rows = _section != Section.Entities
            ? _visibleRuns.Select(r => $"{r.Submitted.ToLocalTime():HH:mm:ss}  {r.Entity}  {r.Status}").ToArray()
            : _entries.Select(EntityRow).ToArray();
        string EntityRow(CatalogEntry entry)
        {
            var latest = _runs.FirstOrDefault(r => r.Entity == entry.Id);
            string name = entry.Id.Length <= nameWidth ? entry.Id : entry.Id[..(nameWidth - 1)] + "…";
            string status = latest?.Status ?? (entry.Available ? "Ready" : "Unavailable");
            return $"{(entry.Kind == "Workflow" ? "W" : "A")}  {name.PadRight(nameWidth)}  {status}  {latest?.DurationLabel}";
        }
        int match = previous is null ? -1 : Array.FindIndex(_visibleRuns,
            r => r.Entity == previous.Entity && r.Submitted == previous.Submitted && r.RunId == previous.RunId);
        if (_rows.SequenceEqual(rows) && _list.Source is not null)
        {
            if (match >= 0 && match != _list.SelectedItem) _list.SelectedItem = match;
            return;
        }
        int selected = _list.SelectedItem ?? 0, top = _list.Viewport.Y;
        _rows = rows;
        _list.SetSource<string>(new(rows));
        if (rows.Length == 0) return;
        _list.SelectedItem = match >= 0 ? match : Math.Clamp(selected, 0, rows.Length - 1);
        _list.ScrollVertical(Math.Clamp(top, 0, rows.Length - 1) - _list.Viewport.Y);
    }

    private void UpdateDetail()
    {
        _detail.Entry = Selected;
        _detail.ActivitySection = _section != Section.Entities;
        _detail.History = _detail.ActivitySection ? (SelectedRun is { } run ? [run] : [])
            : _runs.Where(r => r.Entity == Selected?.Id).ToArray();
        _detail.HistoryMessage = _historyMessage;
        _detail.SetNeedsDraw();
        UpdateHint();
    }

    private void UpdateHint() => _hint.Text = _message.Length > 0 ? _message :
        (FirstExample is not null ? "n open first example   " : "") +
        "1 entities  2 running  3 failures   Enter open" + (_section == Section.Entities && _detail.History.Length > 0 ? "   l latest" : "") +
        (_wide ? "   Ctrl+P actions   q quit" : "   : actions");

    public void Select(string id)
    {
        if (_section != Section.Entities) ShowSection(Section.Entities);
        int index = Array.FindIndex(_entries, item => item.Id == id);
        if (index >= 0) _list.SelectedItem = index;
    }

    public void SetMessage(string message) { _message = message; UpdateHint(); }
    public void FocusList() => _list.SetFocus();
}

internal sealed class CatalogDetail : View
{
    public bool ActivitySection { get; set; }
    public CatalogEntry? Entry { get; set; }
    public CatalogRun[] History { get; set; } = [];
    public string? HistoryMessage { get; set; }
    protected override bool OnDrawingContent(DrawContext? context)
    {

        int width = Viewport.Width, height = Viewport.Height;
        void Line(int y, string value, ColorName16 color = ColorName16.Gray)
        {
            if (width <= 0 || y < 0 || y >= height) return;
            SetAttribute(new(color, ColorName16.Black));
            Move(0, y);
            AddStr(value[..Math.Min(width, value.Length)]);
        }
        if (ActivitySection)
        {
            if (History.FirstOrDefault() is not { } run)
            {
                Line(0, HistoryMessage ?? "No matching runs.");
                return true;
            }
            Line(0, run.Entity, ColorName16.BrightCyan);
            Line(1, $"{run.Status}   {run.DurationLabel}   {run.Events} events");
            Line(2, $"Submitted  {run.Submitted.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            if (run.RunId is { } id) Line(3, id);
            Line(5, "Enter opens this run.");
            return true;
        }
        if (Entry is not { } entry) { Line(0, "No entities in this catalog."); return true; }
        Line(0, $"{entry.Kind.ToUpperInvariant()}  {entry.Id}", ColorName16.BrightCyan);
        int descLines = height >= 10 ? 2 : 1;
        string desc = entry.Description.Replace('\n', ' ');
        int row = 1;
        for (; row <= descLines && desc.Length > 0 && width > 0; row++)
        {
            int count = Math.Min(width, desc.Length);
            if (count < desc.Length)
            {
                int space = desc.LastIndexOf(' ', count - 1, count);
                if (space > 0) count = space;
            }
            Line(row, desc[..count]);
            desc = desc[count..].TrimStart();
        }
        if (height > 10) row++;
        Line(row++, "RUN DURATION   ·   newest first", ColorName16.BrightCyan);
        if (History.Length == 0)
        {
            Line(row++, HistoryMessage ?? "No runs yet.");
            if (HistoryMessage is null && entry.Available)
            {
                Line(row++, "Enter opens this entity. Press r to run.");
                if (entry.InputExample is { Length: > 0 } example && row + 2 < height)
                {
                    Line(++row, "EXAMPLE INPUT", ColorName16.BrightCyan);
                    foreach (string line in example.Split('\n').Take(height - row - 1)) Line(++row, line);
                }
            }
            return true;
        }
        var visible = History.Take(Math.Clamp(height - row - 1, 1, 8)).ToArray();
        double maximum = visible.Max(r => r.Duration?.TotalMilliseconds ?? 0);
        int barWidth = Math.Clamp(width - 31, 2, 20);
        foreach (var run in visible)
        {
            int bars = maximum > 0 && run.Duration is { } d ? Math.Max(1, (int)Math.Round(barWidth * d.TotalMilliseconds / maximum)) : 0;
            string time = $"{run.Submitted.ToLocalTime():HH:mm:ss}";
            string bar = new string('━', bars).PadRight(barWidth);
            var color = run.Status switch
            {
                "Completed" => ColorName16.BrightGreen,
                "Failed" or "Timed out" or "Partially failed" or "Partial result" => ColorName16.BrightRed,
                "Running" or "Queued" => ColorName16.BrightYellow,
                _ => ColorName16.Gray
            };
            Line(row++, $"{time} {bar} {run.DurationLabel,6}  {run.Status}", color);
        }
        Line(row, $"Latest · {History[0].Events} events", ColorName16.White);
        return true;
    }
}
