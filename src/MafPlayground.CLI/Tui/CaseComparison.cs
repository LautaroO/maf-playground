using System.Text.Json;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

internal static class CaseComparison
{
    public static void Open(string entity, string baseline, string candidate,
        Func<string, StudioCase, CancellationToken, Task<CaseOutcome>> execute,
        Action<CaseOutcome, CaseOutcome> compare, CancellationToken token)
    {
        if (baseline == candidate)
        {
            MessageBox.Query(StudioTerminal.App, "Compare models", "Choose two different models or profiles.", "Close");
            return;
        }

        StudioCase[] cases;
        try { cases = StudioFileDialog.Run("Read saved cases", new CaseStore(entity).Read, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.ErrorQuery(StudioTerminal.App, "Saved cases", InspectorRun.Clean(ex.Message), "Close");
            return;
        }
        if (cases.Length == 0) { MessageBox.Query(StudioTerminal.App, "Compare models", "Save a case for this entity first.", "Close"); return; }
        var jobs = Schedule(cases, baseline, candidate, execute);
        string preview = $"Entity  {entity}\nBaseline  {baseline}\nCandidate  {candidate}\n\n" +
            $"{cases.Length} saved cases × 2 selections = {jobs.Length} independent runs.";
        if (MessageBox.Query(StudioTerminal.App, "Run model comparison?", InspectorRun.Clean(preview), "Cancel", "Run suite") != 1) return;

        List<CaseOutcome> outcomes = [];
        CaseLibrary.Run(jobs, outcomes.Add, token);
        if (token.IsCancellationRequested) return;
        using var dialog = new Dialog { Title = "Model comparison", Width = Dim.Percent(96), Height = Dim.Percent(85) };
        dialog.SetScheme(StudioPalette.Create());
        var left = outcomes.Where((_, i) => i % 2 == 0).ToArray();
        var right = outcomes.Where((_, i) => i % 2 == 1).ToArray();
        static string Summary(string selection, CaseOutcome[] side, int count)
        {
            string duration = side.Length > 0 && side.All(r => r.Duration.HasValue)
                ? $"{side.Sum(r => r.Duration!.Value.TotalSeconds):0.###}s" : "—";
            string Sum(Func<CaseOutcome, long?> metric) => side.Length > 0 && side.All(r => metric(r).HasValue)
                ? side.Sum(r => (decimal)metric(r)!.Value).ToString("0") : "—";
            return $"{selection}\n   {side.Count(o => o.Failures.Count == 0)}/{side.Length} passed   {side.Length}/{count} run   {duration}   tokens in/out {Sum(o => o.InputTokens)}/{Sum(o => o.OutputTokens)}";
        }
        string report = $"A  {Summary(baseline, left, cases.Length)}\nB  {Summary(candidate, right, cases.Length)}";
        dialog.Add(new StudioTextView { Text = InspectorRun.Clean(report), ReadOnly = true, WordWrap = true, Width = Dim.Fill(), Height = 5 });
        var list = new ListView { Y = 6, Width = Dim.Fill(), Height = Dim.Fill(1) };
        list.SetSource<string>([.. cases.Select((item, i) =>
            $"{item.Name}   A {Result(left.ElementAtOrDefault(i))}   B {Result(right.ElementAtOrDefault(i))}")]);
        list.Value = 0;
        list.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (list.Value is not int i) return;
            var a = left.ElementAtOrDefault(i);
            var b = right.ElementAtOrDefault(i);
            if (a is not null && b is not null && ((a.Local != null && b.Local != null) || (a.Remote != null && b.Remote != null)))
                compare(a, b);
            else MessageBox.Query(StudioTerminal.App, cases[i].Name, $"A   {Result(a)}\nB   {Result(b)}\nBoth captures are required to compare graphs.", "Close");
        };
        dialog.Add(list, new Label { Text = "Enter compare case graphs   Esc close", Y = Pos.AnchorEnd(1) });
        list.SetFocus();
        StudioTerminal.App.Run(dialog);
    }

    internal static CaseWork[] Schedule(StudioCase[] cases, string baseline, string candidate,
        Func<string, StudioCase, CancellationToken, Task<CaseOutcome>> execute) =>
        cases.SelectMany(item => new[] { baseline, candidate }.Select(selection =>
            new CaseWork($"{selection} · {item.Name}", token => execute(selection, item, token)))).ToArray();

    internal static string Result(CaseOutcome? outcome)
    {
        if (outcome is null) return "not run";
        string elapsed = outcome.Duration is { } duration ? $" {duration.TotalSeconds:0.###}s" : "";
        string tokens = outcome.InputTokens.HasValue || outcome.OutputTokens.HasValue
            ? $" {outcome.InputTokens?.ToString() ?? "—"}/{outcome.OutputTokens?.ToString() ?? "—"} tokens" : "";
        return (outcome.Failures.Count == 0 ? "PASS" : "FAIL " + InspectorRun.Clean(outcome.Failures[0].Message).Replace('\n', ' ')) + elapsed + tokens;
    }
}
