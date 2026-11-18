using System.Collections.Concurrent;
using System.Text.Json;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

internal sealed record CaseOutcome(string Name, List<CaseFailure> Failures, Action? Inspect = null, Action? Remember = null)
{
    public LocalCapture? Local { get; init; }
    public RemoteJournal? Remote { get; init; }
    public TimeSpan? Duration { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
}

internal sealed record CaseWork(string Label, Func<CancellationToken, Task<CaseOutcome>> Execute);

internal sealed class CaseLibrary
{
    private readonly List<CaseOutcome> _results = [];
    private (string Entity, string Context)? _scope;

    public void Open(string entity, string context, string input, Action<string> apply, Func<bool> ready,
        Func<StudioCase, CancellationToken, Task<CaseOutcome>> execute, CancellationToken token)
    {
        try
        {
            if (_scope != (entity, context))
            {
                _results.Clear();
                _scope = (entity, context);
            }
            var store = new CaseStore(entity);
            while (!token.IsCancellationRequested)
            {
                var cases = StudioFileDialog.Run("Read saved cases", store.Read, token);
                int choice = Pick($"Cases · {entity} · {context}", ["Save a new case from current input", $"Run all {cases.Length} cases",
                    "Last suite results", .. cases.Select(item => item.Name)]);
                if (choice < 0) return;
                if (choice == 0)
                {
                    if (Edit(null, input) is { } created) StudioFileDialog.Run("Save case", () => store.Save(created, true), token);
                    continue;
                }
                if (choice == 2) { if (Results()) return; continue; }
                StudioCase[] selected = cases;
                if (choice > 2)
                {
                    var item = cases[choice - 3];
                    switch (Pick(item.Name, ["Use as draft input", "Run this case", "Edit case", "Delete case"]))
                    {
                        case 0: apply(item.Input.Pretty()); return;
                        case 1: selected = [item]; break;
                        case 2:
                            if (Edit(item, item.Input.Pretty()) is { } edited) StudioFileDialog.Run("Save case", () => store.Save(edited, false), token);
                            continue;
                        case 3:
                            if (MessageBox.Query(StudioTerminal.App, "Delete saved case", item.Name, "Keep", "Delete") == 1) StudioFileDialog.Run("Delete case", () => store.Delete(item.Id), token);
                            continue;
                        default: continue;
                    }
                }
                if (selected.Length == 0) { MessageBox.Query(StudioTerminal.App, "Cases", "Save a case first.", "Close"); continue; }
                if (!ready()) continue;
                _results.Clear();
                Run(selected.Select(item => new CaseWork(item.Name, ct => execute(item, ct))).ToArray(), outcome =>
                {
                    _results.Add(outcome);
                    outcome.Remember?.Invoke();
                }, token);
                if (token.IsCancellationRequested || Results()) return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.ErrorQuery(StudioTerminal.App, "Cases", InspectorRun.Clean(ex.Message), "Close");
        }
    }

    public static Button SaveButton(string entity, Func<string> input, CancellationToken token)
    {
        var button = new Button { Text = "Save as case…" };
        button.Accepting += (_, e) =>
        {
            e.Handled = true;
            try
            {
                if (Edit(null, input()) is { } item) StudioFileDialog.Run("Save case", () => new CaseStore(entity).Save(item, create: true), token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                MessageBox.ErrorQuery(StudioTerminal.App, "Save case", InspectorRun.Clean(ex.Message), "Close");
            }
        };
        return button;
    }

    private static StudioCase? Edit(StudioCase? original, string input)
    {
        using var dialog = new Dialog { Title = "Save case locally · omit credentials from input", Width = Dim.Percent(94), Height = Dim.Percent(92) };
        dialog.SetScheme(StudioPalette.Create());
        TextField Field(string label, int y, string? text)
        {
            var field = new TextField { Text = text ?? "", X = 23, Y = y, Width = Dim.Fill(1) };
            dialog.Add(new Label { Text = label, Y = y }, field);
            return field;
        }
        var name = Field("Name", 0, original?.Name);
        var contains = Field("Output contains", 1, original?.Checks.Contains);
        var languages = Field("Languages (es, fr)", 2, string.Join(", ", original?.Checks.Languages ?? []));
        var pointer = Field("Output JSON Pointer", 3, original?.Checks.Pointer);
        var node = Field("Invocation node ID", 4, original?.Checks.Node);
        var maximum = Field("Max calls to node", 5, original?.Checks.MaxInvocations?.ToString());
        var editor = new StudioTextView { Y = 7, Width = Dim.Fill(), Height = Dim.Fill(), TabKeyAddsTab = false, Text = input };
        dialog.Add(new Label { Text = "Input JSON · text checks use the pointer or each translated text", Y = 6 }, editor);
        var save = new Button { Text = "Save" };
        StudioCase? result = null;
        save.Accepting += (_, e) =>
        {
            e.Handled = true;
            try
            {
                static string? Optional(TextField field) => field.Text.ToString()?.Trim() is { Length: > 0 } s ? s : null;
                int? max = null;
                if (Optional(maximum) is { } limit) max = int.TryParse(limit, out int parsed) ? parsed :
                    throw new InvalidDataException("Enter a whole number for the invocation limit.");
                var checks = new CaseChecks(Optional(contains), Optional(languages)?.Split(',', StringSplitOptions.TrimEntries),
                    Optional(pointer), Optional(node), max);
                var item = new StudioCase(original?.Id ?? Guid.NewGuid(), Optional(name) ?? "", JsonSerializer.Deserialize<JsonElement>(
                    editor.Text.ToString() ?? ""), checks);
                item.Validate();
                result = item;
                StudioTerminal.App.RequestStop();
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { MessageBox.ErrorQuery(StudioTerminal.App, "Edit case", ex.Message, "Edit"); }
        };
        dialog.AddButton(save);
        StudioTerminal.App.Run(dialog);
        return result;
    }

    internal static Task ExecuteAsync(StudioCase[] cases, Func<StudioCase, CancellationToken, Task<CaseOutcome>> execute,
        Action<string> started, Action<CaseOutcome> finished, CancellationToken token)
    {
        return ExecuteAsync(cases.Select(item => new CaseWork(item.Name, ct => execute(item, ct))).ToArray(), started, finished, token);
    }

    internal static async Task ExecuteAsync(CaseWork[] cases, Action<string> started, Action<CaseOutcome> finished, CancellationToken token)
    {
        foreach (var item in cases)
        {
            if (token.IsCancellationRequested) break;
            started(item.Label);
            try { finished(await item.Execute(token).ConfigureAwait(false)); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { finished(new(item.Label, [new("Cancelled")])); break; }
            catch (Exception ex)
            {
                // A lost remote receipt or polling failure leaves execution state uncertain.
                finished(new(item.Label, [new(InspectorRun.Clean(ex.Message) + " · suite stopped")]));
                break;
            }
        }
    }

    internal static void Run(CaseWork[] cases, Action<CaseOutcome> finished, CancellationToken token)
    {
        int completed = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var dialog = new Dialog { Title = "Running cases", Width = 62, Height = 9 };
        dialog.SetScheme(StudioPalette.Create());
        var label = new Label { Text = "Starting…", Width = Dim.Fill(), Height = 3 };
        var cancel = new Button { Text = "Cancel suite" };
        cancel.Accepting += (_, e) => { e.Handled = true; stop.Cancel(); };
        dialog.Add(label);
        dialog.AddButton(cancel);
        var updates = new ConcurrentQueue<Action>();
        var task = Task.Run(() => ExecuteAsync(cases,
            name => updates.Enqueue(() => label.Text = $"{completed + 1}/{cases.Length}  {InspectorRun.Clean(name).Replace('\n', ' ')}"),
            outcome => updates.Enqueue(() => { completed++; finished(outcome); }), stop.Token));
        dialog.IsRunningChanging += (_, e) => { if (!e.NewValue && !task.IsCompleted) { stop.Cancel(); e.Cancel = true; } };
        var timer = StudioTerminal.App.AddTimeout(TimeSpan.FromMilliseconds(90), () =>
        {
            if (stop.IsCancellationRequested) label.Text = "Cancelling suite…";
            if (task.IsCompleted) { StudioTerminal.App.RequestStop(dialog); return false; }
            while (updates.TryDequeue(out var update)) update();
            return true;
        });
        try { StudioTerminal.App.Run(dialog); }
        finally { if (timer is not null) StudioTerminal.App.RemoveTimeout(timer); }
        while (updates.TryDequeue(out var update)) update();
    }

    private bool Results()
    {
        if (_results.Count == 0) { MessageBox.Query(StudioTerminal.App, "Suite results", "No cases have run.", "Close"); return false; }
        int passed = _results.Count(r => r.Failures.Count == 0);
        int index = Pick($"Suite results · {passed} passed · {_results.Count - passed} failed",
            _results.Select(r => r.Failures.Count == 0 ? $"PASS  {r.Name}" : $"FAIL  {r.Name}   {r.Failures[0].Message}").ToArray());
        if (index < 0) return false;
        var outcome = _results[index];
        int? selected = MessageBox.Query(StudioTerminal.App, outcome.Name, string.Join('\n', outcome.Failures.Select(f => InspectorRun.Clean(f.Message)))
            is { Length: > 0 } failures ? failures : "All checks passed.", outcome.Inspect is null ? ["Close"] : ["Inspect run", "Back"]);
        if (selected != 0 || outcome.Inspect is null) return false;
        outcome.Inspect();
        return true;
    }

    private static int Pick(string title, string[] choices)
    {
        using var dialog = new Dialog { Title = InspectorRun.Clean(title), Width = Dim.Percent(90), Height = Dim.Percent(75) };
        dialog.SetScheme(StudioPalette.Create());
        var list = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
        list.SetSource<string>([.. choices.Select(s => InspectorRun.Clean(s).Replace('\n', ' '))]);
        list.Value = 0;
        int selected = -1;
        list.Accepting += (_, e) => { e.Handled = true; selected = list.Value ?? -1; StudioTerminal.App.RequestStop(); };
        dialog.Add(list);
        StudioTerminal.App.Run(dialog);
        return selected;
    }
}
