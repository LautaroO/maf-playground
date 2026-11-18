using System.Text.Json;
using MafPlayground.CLI.Inspection;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;

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
            else MessageBox.Query(StudioTerminal.App, "Errors", "No rejected or failed invocations.", "Close");
            return;
        }
        int current = issues.FindIndex(item => item.Id == _graph.Nodes[_node].Id && item.index == _attempt);
        var next = issues[(current + 1) % issues.Count];
        Inspect(next.Id, next.index);
    }

    private bool ChooseModel()
    {
        if (PromptModel("Local model", _model) is not { } selection) return false;
        _model = selection;
        return true;
    }

    private static string? PromptModel(string title, string? initial)
    {
        using var dialog = new Dialog { Title = title, Width = 60, Height = 9 };
        var value = new TextField { Text = initial ?? "", X = 1, Y = 2, Width = Dim.Fill(1) };
        var hint = new Label { Text = "Enter apply   Esc cancel", X = 1, Y = 3, Width = Dim.Fill(1) };
        var save = new Button { Text = "Apply", IsDefault = true };
        string? applied = null;
        save.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (!AIModelSelection.TryParse(value.Text.ToString(), out var model))
            {
                hint.Text = "Use provider:model (for example ollama:llama3.1:8b).";
                value.SetFocus();
                return;
            }
            applied = model.ToString();
            StudioTerminal.App.RequestStop();
        };
        dialog.Add(new Label { Text = "Provider and model (provider:model)" }, value, hint);
        dialog.AddButton(save);
        value.SetFocus();
        StudioTerminal.App.Run(dialog);
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
            MessageBox.Query(StudioTerminal.App, "History", "Run or open a capture first.", "Close");
            return;
        }
        var choices = compare ? _history.Where(c => c.Started != _started || c.Entity != _entity.Id).ToList() : _history;
        if (choices.Count == 0)
        {
            MessageBox.Query(StudioTerminal.App, "Comparison", "Run another case or open a second capture to compare.", "Close");
            return;
        }
        using var dialog = new Dialog { Title = compare ? "Select baseline" : "Run history", Width = 66, Height = 16 };
        var list = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
        list.SetSource<string>(new(choices.Select(c => $"{c.Started:HH:mm:ss}   {c.Entity}   {c.Status}")));
        list.SelectedItem = 0;
        LocalCapture? picked = null;
        list.Accepting += (_, e) => { e.Handled = true; picked = choices[list.SelectedItem ?? 0]; StudioTerminal.App.RequestStop(); };
        dialog.Add(list);
        StudioTerminal.App.Run(dialog);
        if (picked is null) return;
        if (compare)
        {
            var candidate = CurrentCapture();
            GraphInspectionDialog.Compare(GraphEvidence.From(picked), GraphEvidence.From(candidate), LocalCapture.Compare(picked, candidate));
        }
        else Load(picked);
    }

    private void Replay() => GraphInspectionDialog.Replay(GraphEvidence.From(CurrentCapture()));

    private static void ShowText(string title, string text)
    {
        using var dialog = new Dialog { Title = title, Width = Dim.Percent(92), Height = Dim.Percent(85) };
        dialog.Add(new StudioTextView { Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true, WordWrap = true, Text = text });
        StudioTerminal.App.Run(dialog);
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
            MessageBox.Query(StudioTerminal.App, "Save", "Run or open a capture first.", "Close");
            return;
        }
        try
        {
            using FileDialog dialog = save
                ? new SaveDialog { Title = "Save capture · Includes local inputs, outputs and errors. Choose a new file." }
                : new OpenDialog { Title = "Open capture" };
            dialog.AllowedTypes.Add(new AllowedType("JSON capture", ".json"));
            StudioTerminal.App.Run(dialog);
            if (dialog.Canceled || dialog.Path is not { Length: > 0 } path) return;
            if (save)
            {
                var capture = CurrentCapture();
                StudioFileDialog.Run("Save capture", () => capture.Save(path), _token);
            }
            else
            {
                var capture = StudioFileDialog.Run("Open capture", () => LocalCapture.Read(path), _token);
                ArchiveCurrent();
                Load(capture);
                Remember(capture);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.ErrorQuery(StudioTerminal.App, "Capture", InspectorRun.Clean(ex.Message), "Close");
        }
    }
}
