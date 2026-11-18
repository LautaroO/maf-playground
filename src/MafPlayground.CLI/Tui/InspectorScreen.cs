using System.Collections.Concurrent;
using MafPlayground.CLI.Inspection;
using Microsoft.Extensions.Configuration;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace MafPlayground.CLI.Tui;

internal sealed partial class InspectorScreen : View
{
    private readonly IConfiguration _configuration;
    private readonly Func<LocalEntityDescriptor, string, string?, Action<Action<InspectorRun>>, CancellationToken, Task> _execute;
    private readonly CancellationToken _token;
    private readonly ConcurrentQueue<Action<InspectorRun>> _updates = new();
    private LocalEntityDescriptor _entity;
    private string _input;
    private WorkflowGraph _graph;
    private InspectorRun _run = new();
    private int _node, _attempt, _page, _detail, _scroll;
    private bool _inspect;
    public event Action? CatalogRequested;
    public Task Pending { get; private set; } = Task.CompletedTask;

    public InspectorScreen(LocalEntityDescriptor entity, string? input, IConfiguration configuration, string? model,
        Func<LocalEntityDescriptor, string, string?, Action<Action<InspectorRun>>, CancellationToken, Task> execute, CancellationToken token)
    {
        _entity = entity;
        _configuration = configuration;
        _execute = execute;
        _token = token;
        _model = model;
        _input = input ?? InspectorRun.Format(entity.InputExample);
        _graph = Graph(entity);
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();
    }

    private WorkflowGraph Graph(LocalEntityDescriptor d) => d.CreateWorkflowDiagram is null
        ? new(d.Id, [d.Id], []) : WorkflowGraph.From(d.CreateWorkflowDiagram(_configuration));

    public void Start()
    {
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(80), _ =>
        {
            bool changed = false;
            while (_updates.TryDequeue(out var update)) { update(_run); changed = true; }
            if (changed && _entity.Kind == LocalEntityKind.Agent && _graph.Nodes.Count != _run.ToolNodes.Count + 1)
            {
                string selected = _graph.Nodes[_node].Id;
                _graph = new WorkflowGraph(_entity.Id, _run.ToolNodes.Prepend(_entity.Id),
                    _run.ToolNodes.Select(tool => (_entity.Id, tool)));
                _node = Math.Max(0, _graph.Nodes.ToList().FindIndex(n => n.Id == selected));
            }
            if (changed || _run.Status == "Running") SetNeedsDisplay();
            return true;
        });
    }

    private void Run()
    {
        if (!Pending.IsCompleted || !_updates.IsEmpty) return;
        if (_entity.CreateAgent is null && _entity.StreamWorkflow is null)
        {
            MessageBox.Query("Archived entity", "This entity is unavailable in the local catalog.", "Close");
            return;
        }
        if (!AIModelSelection.TryParse(_model, out _) && !ChooseModel()) return;
        ArchiveCurrent();
        _run = new InspectorRun();
        _graph = Graph(_entity);
        _node = Math.Min(_node, _graph.Nodes.Count - 1);
        _runModel = _model;
        _runInput = _input;
        _started = DateTimeOffset.Now;
        _archived = false;
        _eventIndex = 0;
        _job?.Dispose();
        _job = CancellationTokenSource.CreateLinkedTokenSource(_token);
        var jobToken = _job.Token;
        var model = _model;
        _run.Start();
        _attempt = _scroll = 0;
        var entity = _entity;
        var input = _input;
        Pending = Task.Run(async () =>
        {
            try { await _execute(entity, input, model, _updates.Enqueue, jobToken); }
            catch (OperationCanceledException) when (jobToken.IsCancellationRequested) { _updates.Enqueue(run => run.Cancel()); }
            catch (Exception error) { _updates.Enqueue(run => run.Fail(error.Message)); }
        });
    }

    private void Edit()
    {
        if (!Pending.IsCompleted) return;
        using var dialog = new Dialog { Title = "Run input · JSON", Width = Dim.Percent(90), Height = Dim.Percent(75) };
        var editor = new TextView { Width = Dim.Fill(), Height = Dim.Fill(3), AllowsTab = false, Text = _input };
        var save = new Button("Apply");
        save.Clicked += () =>
        {
            string json = editor.Text.ToString() ?? "";
            try
            {
                using var parsed = System.Text.Json.JsonDocument.Parse(json);
                _input = json;
                Application.RequestStop();
            }
            catch (System.Text.Json.JsonException ex) { MessageBox.ErrorQuery("Invalid JSON", ex.Message, "Edit"); }
        };
        dialog.Add(editor, new Label("Tab → Apply    Enter confirm    Esc cancel") { Y = Pos.Bottom(editor) });
        dialog.AddButton(save);
        Application.Run(dialog);
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        string id = _graph.Nodes[_node].Id;
        switch (keyEvent.Key)
        {
            case Key.Q: case (Key)'q': Application.RequestStop(); break;
            case (Key)'r': Run(); break;
            case (Key)'x': if (!Pending.IsCompleted) _job?.Cancel(); break;
            case (Key)'m': if (Pending.IsCompleted) ChooseModel(); break;
            case (Key)'e': NextError(); break;
            case (Key)'h': History(compare: false); break;
            case (Key)'b': History(compare: true); break;
            case (Key)'s': CaptureFile(save: true); break;
            case (Key)'o': CaptureFile(save: false); break;
            case Key.Enter when _page == 1:
                if (_run.EventTarget(_eventIndex) is { } target) Inspect(target.Id, target.Index);
                break;
            case (Key)'c': case Key.Esc: CatalogRequested?.Invoke(); break;
            case (Key)'i': Edit(); break;
            case (Key)'v': ShowText("Input schema", InspectorRun.Format(EntityInputRenderer.Schema(_entity))); break;
            case (Key)'1':
            case (Key)'2':
            case (Key)'3':
                _page = (int)keyEvent.Key - '1'; _scroll = 0; _inspect = false; break;
            case Key.Enter: case Key.Tab: _inspect = !_inspect; _scroll = 0; break;
            case (Key)'[': _attempt = Math.Max(0, _attempt - 1); _scroll = 0; break;
            case (Key)']': _attempt = Math.Min(Math.Max(0, _run.Attempts(id).Count - 1), _attempt + 1); _scroll = 0; break;
            case Key.CursorLeft:
            case Key.CursorRight:
                int direction = keyEvent.Key == Key.CursorRight ? 1 : -1;
                if (_inspect) { _detail = (_detail + direction + 3) % 3; _scroll = 0; }
                else FollowEdge(id, direction > 0);
                break;
            case Key.CursorUp:
            case Key.CursorDown:
                int delta = keyEvent.Key == Key.CursorDown ? 1 : -1;
                if (_page == 1) _eventIndex = Math.Clamp(_eventIndex + delta, 0, Math.Max(0, _run.Events.Count - 1));
                else if (_inspect || _page != 0) _scroll = Math.Max(0, _scroll + delta);
                else Select((_node + delta + _graph.Nodes.Count) % _graph.Nodes.Count);
                break;
            case (Key)'?':
                MessageBox.Query("Keys", "c catalog   i edit input   v input schema   r run\nm model   x cancel   e next error\nh history   b compare   s save   o open\n1 graph   2 events (Enter inspect)   3 result\n↑↓ node / scroll   ←→ edge / detail tab\nTab inspector   [ ] invocation   Esc catalog   q quit", "Close");
                break;
            default: return base.ProcessKey(keyEvent);
        }
        SetNeedsDisplay();
        return true;
    }

    private void FollowEdge(string id, bool forward)
    {
        var neighbors = _graph.Neighbors(id, forward).ToArray();
        if (neighbors.Length == 0) return;
        string? target = neighbors[0];
        if (neighbors.Length > 1)
        {
            target = null;
            using var dialog = new Dialog(forward ? "Outgoing edges" : "Incoming edges", 48, 12);
            var choices = new ListView(neighbors) { Width = Dim.Fill(), Height = Dim.Fill() };
            choices.OpenSelectedItem += _ =>
            {
                target = neighbors[choices.SelectedItem];
                Application.RequestStop();
            };
            dialog.Add(choices);
            Application.Run(dialog);
        }
        if (target is not null) Select(_graph.Nodes.ToList().FindIndex(n => n.Id == target));
    }

    private void Select(int index)
    {
        _node = index;
        var attempts = _run.Attempts(_graph.Nodes[index].Id);
        _attempt = Math.Max(0, attempts.Count - 1);
        _detail = attempts.LastOrDefault() is { Rejected: true } or { Failed: true } ? 2 : 0;
        _scroll = 0;
    }

    private static Attribute Ink(Color color, Color background = Color.Black) => Application.Driver.MakeAttribute(color, background);

    private void Print(int x, int y, string text, Attribute ink, int width)
    {
        if (y < 0 || y >= Bounds.Height || x < 0 || x >= Bounds.Width) return;
        width = Math.Min(width, Bounds.Width - x);
        if (width <= 0) return;
        Move(x, y);
        Application.Driver.SetAttribute(ink);
        Application.Driver.AddStr(text[..Math.Min(width, text.Length)]);
    }

    public override void PositionCursor() => Application.Driver.SetCursorVisibility(CursorVisibility.Invisible);

    public override void Redraw(Rect bounds)
    {
        var dim = Ink(Color.Gray);
        var accent = Ink(Color.BrightCyan);
        var white = Ink(Color.White);
        Application.Driver.SetAttribute(Ink(Color.Black));
        Clear();
        int w = Bounds.Width, h = Bounds.Height;
        Print(1, 0, " MAF ", Ink(Color.Black, Color.BrightCyan), w - 2);
        Print(8, 0, $"studio / {_entity.Id}", white, w - 9);
        Print(w - 8, 0, "● local", accent, 7);
        if (_run.Status == "Running" && _run.Elapsed is { } runningTime)
        {
            const string frames = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";
            char frame = frames[(int)(runningTime.TotalMilliseconds / 120 % frames.Length)];
            string phase = _run.Events.Count == 0 ? "  Waiting for first event" : "";
            Print(1, 1, $@" {frame} RUNNING  {runningTime:mm\:ss}{phase} ", Ink(Color.Black, Color.BrightCyan), w - 2);
        }
        Print(2, 2, "1 Graph     2 Events     3 Result", dim, w);
        int[] tabOffsets = [2, 14, 27];
        Print(tabOffsets[_page], 2, new[] { "1 Graph", "2 Events", "3 Result" }[_page], accent, w);
        if (_page == 0)
        {
            bool split = w >= 96;
            int boundary = split ? w * 3 / 5 : w;
            if (split)
                for (int row = 4; row < h - 3; row++) Print(boundary, row, "│", Ink(Color.DarkGray), 1);
            if (split || !_inspect)
            {
                Print(2, 4, _entity.Kind.ToString().ToUpperInvariant(), dim, boundary - 3);
                if (_run.Status == "Running") Print(14, 4, "▶ executing", Ink(Color.BrightYellow), boundary - 15);
                DrawGraph(boundary, h);
            }
            if (split || _inspect) DrawInspector(split ? boundary : 0, w, h);
        }
        else if (_page == 1)
        {
            int rows = Math.Max(1, h - 8);
            int first = Math.Max(0, _eventIndex - rows + 1);
            for (int i = first; i < Math.Min(first + rows, _run.Events.Count); i++)
                Print(2, 5 + i - first, _run.Events[i], i == _eventIndex ? Ink(Color.Black, Color.BrightCyan) : white, w - 4);
        }
        else DrawText(2, 5, w - 4, h - 8, _run.Result, white);
        string summary = _run.Status == "Failed" ? _run.Result.Split('\n')[0] : $"{_run.Events.Count} events";
        Print(2, h - 2, $"{_run.Status}   ·   {summary}", _run.Status is "Failed" or "Partial result" ? Ink(Color.BrightRed) : accent, w - 14);
        if (_run.Elapsed is { } elapsed) Print(w - 10, h - 2, elapsed.ToString(@"mm\:ss\.ff"), dim, 8);
        string controls = _run.Status == "Running" ? " x cancel" : " r run";
        Print(1, h - 1, controls + "   Esc catalog   i input   m model   e next error   h history   ? keys   q quit", dim, w - 2);
    }

    private void DrawGraph(int width, int height)
    {
        var selected = _graph.Nodes[_node];
        int dx = Math.Max(0, selected.X + selected.Id.Length + 4 - width);
        int dy = Math.Max(0, selected.Y + 10 - height);
        for (int row = dy; row < _graph.Lines.Length && row - dy + 6 < height - 3; row++)
        {
            string line = _graph.Lines[row];
            if (dx < line.Length) Print(1, row - dy + 6, line[dx..], Ink(Color.DarkGray), width - 2);
        }
        foreach (var node in _graph.Nodes)
        {
            int x = node.X - dx + 1, y = node.Y - dy + 6;
            if (x < 0 || y < 6 || y >= height - 3) continue;
            var last = _run.Attempts(node.Id).LastOrDefault();
            var color = last is { Rejected: true } or { Failed: true } ? Color.BrightRed : last is { Finished: true } ? Color.BrightGreen : Color.White;
            if (_run.IsActive(node.Id))
            {
                Print(x - 1, y, "▶", Ink(Color.BrightYellow), 1);
                Print(x, y, node.Id, Ink(Color.Black, node == selected ? Color.BrightCyan : Color.BrightYellow), width - x - 1);
            }
            else Print(x, y, node.Id, node == selected ? Ink(Color.Black, Color.BrightCyan) : Ink(color), width - x - 1);
        }
    }

    private void DrawInspector(int left, int width, int height)
    {
        int x = left + 2, available = width - x - 1;
        string id = _graph.Nodes[_node].Id;
        var attempts = _run.Attempts(id);
        Print(x, 4, _inspect ? "▸ INSPECTOR" : "INSPECTOR", Ink(Color.BrightCyan), available);
        Print(x, 6, id, Ink(Color.White), available);
        if (attempts.Count == 0) { Print(x, 8, "No invocation observed", Ink(Color.Gray), available); return; }
        var attempt = attempts[_attempt];
        Print(x, 8, $"Invocation {_attempt + 1}/{attempts.Count} · {(attempt.Rejected ? "rejected" : attempt.Failed ? "failed" : attempt.Finished ? "done" : "running")}", Ink(Color.Gray), available);
        if (attempt.Duration is { } duration) Print(x, 9, $"Duration {duration.TotalMilliseconds:N0} ms", Ink(Color.Gray), available);
        string[] tabs = ["Input", "Output", "Error"];
        Print(x, 10, string.Join("   ", tabs.Select((name, i) => i == _detail ? $"[{name}]" : name)), Ink(Color.BrightCyan), available);
        string content = _detail switch { 0 => attempt.Input, 1 => attempt.Output, _ => attempt.Error };
        DrawText(x, 12, available, height - 15, content.Length == 0 ? "—" : content, Ink(Color.White));
    }

    private void DrawText(int x, int y, int width, int height, string text, Attribute ink)
    {
        if (width < 1 || height < 1) return;
        var lines = new List<string>();
        foreach (string line in text.Split('\n'))
        {
            if (line.Length == 0) lines.Add("");
            for (int offset = 0; offset < line.Length; offset += width)
                lines.Add(line.Substring(offset, Math.Min(width, line.Length - offset)));
        }
        _scroll = Math.Min(_scroll, Math.Max(0, lines.Count - height));
        foreach (string line in lines.Skip(_scroll).Take(height)) Print(x, y++, line, ink, width);
    }
}
