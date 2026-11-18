using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

internal sealed record InvocationPair(int Occurrence, GraphInvocation? Baseline, GraphInvocation? Candidate);

internal static class GraphInspectionDialog
{
    public static void Replay(GraphEvidence run)
    {
        if (run.Events.Length == 0)
        {
            MessageBox.Query(StudioTerminal.App, "Replay", "No retained events to replay.", "Close");
            return;
        }
        using var dialog = Window("Graph replay · " + run.Label);
        var list = new ListView { Width = Dim.Percent(32), Height = Dim.Fill(1) };
        list.SetSource<string>([.. run.Events.Select(e => InspectorRun.Clean(e.Label).Replace('\n', ' '))]);
        list.Value = 0;
        var frame = Reader(Pos.Right(list), 0, Dim.Fill(), Dim.Fill(1));
        void Draw() => frame.Text = InspectorRun.Clean(run.ReplayFrame(list.Value ?? 0));
        list.ValueChanged += (_, _) => Draw();
        dialog.Add(list, frame, new Label { Text = "↑/↓ step event    ▶ active   ✓ completed   × failed   ■ cancelled    Esc close", Y = Pos.AnchorEnd(1) });
        Draw();
        StudioTerminal.App.Run(dialog);
    }

    public static InvocationPair[] Pair(GraphEvidence baseline, GraphEvidence candidate, string node)
    {
        var left = baseline.Calls.GetValueOrDefault(node) ?? [];
        var right = candidate.Calls.GetValueOrDefault(node) ?? [];
        return Enumerable.Range(0, Math.Max(left.Length, right.Length)).Select(i =>
            new InvocationPair(i + 1, left.ElementAtOrDefault(i), right.ElementAtOrDefault(i))).ToArray();
    }

    public static void Compare(GraphEvidence baseline, GraphEvidence candidate, string summary,
        Func<string, InvocationPair[]>? matched = null, bool truncated = false)
    {
        string[] nodes = baseline.Nodes.Union(candidate.Nodes).ToArray();
        var pairs = nodes.ToDictionary(node => node, node => matched?.Invoke(node) ?? Pair(baseline, candidate, node));
        string Marker(string node)
        {
            if (!baseline.Nodes.Contains(node)) return "+";
            if (!candidate.Nodes.Contains(node)) return "−";
            var calls = pairs[node];
            if (calls.Length == 0) return "·";
            if (calls.Any(p => p.Baseline is null && p.Candidate is null)) return "?";
            return calls.Any(p => p.Baseline is null || p.Candidate is null || !p.Baseline.SameEvidence(p.Candidate)) ? "Δ" : "=";
        }
        string Decorate(string node) => Marker(node) + " " + node;
        using var dialog = Window(truncated ? "Graph comparison · partial invocation or change coverage" : "Graph comparison");
        var list = new ListView { Width = Dim.Percent(22), Height = Dim.Fill(2) };
        list.SetSource<string>(["Run summary", .. nodes.Select(node => InspectorRun.Clean(Decorate(node)).Replace('\n', ' '))]);
        list.Value = 0;
        var baselineView = Reader(Pos.Right(list), 0, Dim.Percent(39), Dim.Fill(2));
        var candidateView = Reader(Pos.Right(baselineView), 0, Dim.Fill(), Dim.Fill(2));
        string Diagram(GraphEvidence run, string title) => $"{title}  {run.Label}\n{run.Coverage}\n{run.Diagram(Decorate)}";
        baselineView.Text = InspectorRun.Clean(Diagram(baseline, "Baseline"));
        candidateView.Text = InspectorRun.Clean(Diagram(candidate, "Candidate"));
        list.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (list.Value is not int index) return;
            if (index == 0)
            {
                using var details = Window("Run summary");
                var text = Reader(0, 0, Dim.Fill(), Dim.Fill());
                text.Text = InspectorRun.Clean(summary);
                details.Add(text);
                StudioTerminal.App.Run(details);
            }
            else
            {
                var node = nodes[index - 1];
                InspectPairs(node, pairs[node]);
            }
        };
        dialog.Add(list, baselineView, candidateView, new Label { Text = "+/− node added/removed   Δ changed I/O or status   = same retained evidence   · unobserved", Y = Pos.AnchorEnd(2) },
            new Label { Text = "↑/↓ select node   Enter inspect invocations or summary   Tab scroll diagrams   Esc close", Y = Pos.AnchorEnd(1) });
        StudioTerminal.App.Run(dialog);
    }

    private static void InspectPairs(string node, InvocationPair[] pairs)
    {
        if (pairs.Length == 0)
        {
            MessageBox.Query(StudioTerminal.App, InspectorRun.Clean(node), "No matched or retained invocations.", "Close");
            return;
        }
        using var dialog = Window("Invocation evidence · " + node);
        var list = new ListView { Width = 13, Height = Dim.Fill(1) };
        list.SetSource<string>([.. pairs.SelectMany(p => new[] { $"#{p.Occurrence} input", $"#{p.Occurrence} output", $"#{p.Occurrence} error" })]);
        list.Value = 0;
        var left = Reader(Pos.Right(list), 0, Dim.Percent(46), Dim.Fill(1));
        var right = Reader(Pos.Right(left), 0, Dim.Fill(), Dim.Fill(1));
        void Draw()
        {
            int index = list.Value ?? 0;
            var pair = pairs[index / 3];
            string Details(GraphInvocation? call)
            {
                if (call is null) return "(no retained invocation)";
                var payload = (index % 3) switch { 0 => call.Input, 1 => call.Output, _ => call.Error };
                return $"Invocation {call.Id} · {call.Status}\n\n{InspectorRun.Clean(payload)}";
            }
            left.Text = "Baseline\n" + Details(pair.Baseline);
            right.Text = "Candidate\n" + Details(pair.Candidate);
        }
        list.ValueChanged += (_, _) => Draw();
        dialog.Add(list, left, right, new Label { Text = "↑/↓ invocation input, output or error   Tab scroll evidence   Esc back", Y = Pos.AnchorEnd(1) });
        Draw();
        StudioTerminal.App.Run(dialog);
    }

    private static Dialog Window(string title)
    {
        var dialog = new Dialog { Title = InspectorRun.Clean(title), Width = Dim.Percent(96), Height = Dim.Percent(92) };
        dialog.SetScheme(StudioPalette.Create());
        return dialog;
    }

    private static StudioTextView Reader(Pos x, Pos y, Dim width, Dim height) =>
        new() { X = x, Y = y, Width = width, Height = height, ReadOnly = true, TabKeyAddsTab = false };
}
