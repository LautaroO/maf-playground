using System.Text.Json;
using MafPlayground.CLI.Inspection;
using Terminal.Gui;

namespace MafPlayground.CLI.Tui;

internal sealed partial class InspectorScreen
{
    private readonly List<LocalCapture> _history = [];
    private string? _model;
    private string? _runModel;
    private string _runInput = "";
    private DateTimeOffset _started;
    private CancellationTokenSource? _job;
    private int _eventIndex;
    private bool _archived;

    public async Task FinishAsync()
    {
        try
        {
            if (_job is not null) await _job.CancelAsync().ConfigureAwait(false);
            await Pending.ConfigureAwait(false);
        }
        finally { _job?.Dispose(); }
    }

    private void Inspect(string node, int index)
    {
        int position = _graph.Nodes.ToList().FindIndex(n => n.Id == node);
        var attempts = _run.Attempts(node);
        if (position < 0 || index < 0 || index >= attempts.Count) return;
        Select(position);
        _attempt = index;
        _page = 0;
        _inspect = true;
        _detail = attempts[index].Failed || attempts[index].Rejected ? 2 : 1;
    }

    private void NextError()
    {
        var issues = _graph.Nodes.SelectMany(node => _run.Attempts(node.Id)
            .Select((attempt, index) => (node.Id, index, attempt))
            .Where(item => item.attempt.Failed || item.attempt.Rejected)).ToList();
        if (issues.Count == 0)
        {
            if (_run.Status == "Failed") { _page = 2; _scroll = 0; }
            else MessageBox.Query("Errors", "No rejected or failed invocations.", "Close");
            return;
        }
        int current = issues.FindIndex(item => item.Id == _graph.Nodes[_node].Id && item.index == _attempt);
        var next = issues[(current + 1) % issues.Count];
        Inspect(next.Id, next.index);
    }

    private bool ChooseModel()
    {
        using var dialog = new Dialog("Local model", 60, 9);
        var value = new TextField(_model ?? "") { X = 1, Y = 2, Width = Dim.Fill(1) };
        var save = new Button("Apply", is_default: true);
        bool applied = false;
        save.Clicked += () =>
        {
            if (!AIModelSelection.TryParse(value.Text.ToString(), out var model))
            {
                MessageBox.ErrorQuery("Model", "Enter provider:model (for example ollama:llama3.1:8b).", "Edit");
                return;
            }
            _model = model.ToString();
            applied = true;
            Application.RequestStop();
        };
        dialog.Add(new Label("Provider and model (provider:model)"), value);
        dialog.AddButton(save);
        Application.Run(dialog);
        return applied;
    }

    private LocalCapture CurrentCapture() => _run.Capture(_entity.Id, _runModel, _runInput, _started, _graph);

    private void ArchiveCurrent()
    {
        if (_archived || _run.Status is "Ready" or "Running") return;
        Remember(CurrentCapture());
        _archived = true;
    }

    private void Remember(LocalCapture capture)
    {
        _history.Insert(0, capture);
        if (_history.Count > 12) _history.RemoveAt(_history.Count - 1);
    }

    private void History(bool compare)
    {
        if (!Pending.IsCompleted || !_updates.IsEmpty) return;
        ArchiveCurrent();
        if (_history.Count == 0 || (compare && _run.Status == "Ready"))
        {
            MessageBox.Query("History", "Run or open a capture first.", "Close");
            return;
        }
        using var dialog = new Dialog(compare ? "Select baseline" : "Run history", 66, 16);
        var list = new ListView(_history.Select(c => $"{c.Started:HH:mm:ss}   {c.Entity}   {c.Status}").ToArray())
        { Width = Dim.Fill(), Height = Dim.Fill() };
        LocalCapture? picked = null;
        list.OpenSelectedItem += _ => { picked = _history[list.SelectedItem]; Application.RequestStop(); };
        dialog.Add(list);
        Application.Run(dialog);
        if (picked is null) return;
        if (compare)
        {
            ShowText("Comparison · baseline → current", LocalCapture.Compare(picked, CurrentCapture()));
        }
        else Load(picked);
    }

    private static void ShowText(string title, string text)
    {
        using var dialog = new Dialog { Title = title, Width = Dim.Percent(92), Height = Dim.Percent(85) };
        dialog.Add(new TextView { Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, WordWrap = true, Text = text });
        Application.Run(dialog);
    }

    private void Load(LocalCapture capture)
    {
        var entity = LocalEntityCatalog.All.FirstOrDefault(d => d.Id == capture.Entity)
            ?? new LocalEntityDescriptor(capture.Entity,
                capture.Nodes.Length > capture.Tools.Length + 1 ? LocalEntityKind.Workflow : LocalEntityKind.Agent,
                "Archived entity", typeof(JsonElement), new { });
        var graph = capture.Graph();
        var run = InspectorRun.Restore(capture);
        _entity = entity;
        _model = _runModel = capture.Model;
        _input = _runInput = capture.Input;
        _started = capture.Started;
        _graph = graph;
        _run = run;
        _node = _page = _attempt = _detail = _scroll = _eventIndex = 0;
        _inspect = false;
        _archived = true;
    }

    private void CaptureFile(bool save)
    {
        if (!Pending.IsCompleted || !_updates.IsEmpty) return;
        if (save && _run.Status == "Ready")
        {
            MessageBox.Query("Save", "Run or open a capture first.", "Close");
            return;
        }
        try
        {
            using FileDialog dialog = save
                ? new SaveDialog("Save capture", "Includes local inputs, outputs and errors. Choose a new file.", [".json"])
                : new OpenDialog("Open capture", "Inspect a previously saved TUI capture.", [".json"]);
            Application.Run(dialog);
            if (dialog.Canceled || dialog.FilePath.ToString() is not { Length: > 0 } path) return;
            if (save) CurrentCapture().Save(path);
            else
            {
                var capture = LocalCapture.Read(path);
                ArchiveCurrent();
                Load(capture);
                Remember(capture);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.ErrorQuery("Capture", InspectorRun.Clean(ex.Message), "Close");
        }
    }
}
