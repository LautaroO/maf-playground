using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MafPlayground.CLI.Tui;

internal sealed record TimelineEntry(string Node, int Occurrence, GraphInvocation Call, int First, int? Start, int? End, bool Concurrent)
{
    public string Label => $"{Node} #{Occurrence}{ResultPresentation.LanguageSuffix(Node, Call.Input)}";
    public string Bar(int eventCount, double? totalMilliseconds)
    {
        const int width = 12;
        double left, right;
        if (totalMilliseconds is { } total)
        {
            if (Call.StartOffset is not { } offset || Call.Duration is not { } duration) return new string('·', width);
            left = offset.TotalMilliseconds / total;
            right = (offset.TotalMilliseconds + duration.TotalMilliseconds) / total;
        }
        else
        {
            if (Start is null || End is null) return new string('·', width);
            left = (Start.Value - 1d) / Math.Max(1, eventCount - 1);
            right = (End.Value - 1d) / Math.Max(1, eventCount - 1);
        }
        int first = (int)(Math.Clamp(left, 0, 1) * (width - 1));
        int last = Math.Max(first, (int)(Math.Clamp(right, 0, 1) * (width - 1)));
        return new string(' ', first) + new string('━', last - first + 1) + new string(' ', width - last - 1);
    }

    public string Details => $"{Label}\nInvocation {Call.Id}   ·   {Call.Status}\n" +
        $"Start offset   {(Call.StartOffset is { } start ? $"{start.TotalMilliseconds:N0} ms" : "unavailable")}\n" +
        $"Duration   {(Call.Duration is { } duration ? $"{duration.TotalMilliseconds:N0} ms" : "unavailable")}\n" +
        $"Retained event positions   {Start?.ToString() ?? "?"} → {End?.ToString() ?? "?"}\n" +
        $"Overlap   {(Concurrent ? "observed" : "none observed")}\n\nInput\n{Call.Input}\n\nOutput\n{Call.Output}" +
        (Call.Error.Length == 0 ? "" : $"\n\nError\n{Call.Error}");
}

internal static class InvocationTimeline
{
    public static TimelineEntry[] Entries(GraphEvidence evidence)
    {
        var events = evidence.Events.Select((item, index) => (item, order: index + 1))
            .ToLookup(x => (x.item.Node, x.item.InvocationId));
        var entries = (from node in evidence.Calls
                       from item in node.Value.Select((call, index) => (call, occurrence: index + 1))
                       let observations = events[(node.Key, item.call.Id)]
                       let start = observations.Where(e => e.item.State == "Active").Select(e => (int?)e.order).FirstOrDefault()
                       let end = observations.Where(e => e.item.State is "Completed" or "Failed" or "Cancelled").Select(e => (int?)e.order).LastOrDefault()
                       select new TimelineEntry(node.Key, item.occurrence, item.call, observations.Select(e => e.order).DefaultIfEmpty(int.MaxValue).First(),
                           start, end, false)).OrderBy(entry => entry.First).ToArray();
        // Only claim overlap when both lifetimes are observed across the same event.
        // For missing boundaries use the first and last correlated observations.
        int longestEnd = -1, longestIndex = -1;
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.First == int.MaxValue) continue;
            int last = entry.End ?? (entry.Call.Status == "Active" && evidence.Coverage.Length == 0 ? evidence.Events.Length :
                events[(entry.Node, entry.Call.Id)].Last().order);
            if (entry.First < longestEnd || (entry.First == longestEnd && entries[longestIndex].End is null))
            {
                entries[i] = entry with { Concurrent = true };
                entries[longestIndex] = entries[longestIndex] with { Concurrent = true };
            }
            if (last > longestEnd) (longestEnd, longestIndex) = (last, i);
        }
        return entries;
    }

    public static double? TimeScale(TimelineEntry[] entries, TimeSpan? elapsed)
    {
        var measured = entries.Where(e => e.Call.StartOffset >= TimeSpan.Zero && e.Call.Duration >= TimeSpan.Zero)
            .Select(e => e.Call.StartOffset!.Value.TotalMilliseconds + e.Call.Duration!.Value.TotalMilliseconds).ToArray();
        return measured.Length == 0 ? null : Math.Max(1, Math.Max(measured.Max(), elapsed?.TotalMilliseconds ?? 0));
    }

    public static void Show(GraphEvidence evidence)
    {
        var entries = Entries(evidence);
        if (entries.Length == 0) { MessageBox.Query(StudioTerminal.App, "Invocation timeline", "No invocations observed.", "Close"); return; }
        using var dialog = new Dialog { Title = "Invocation timeline · " + evidence.Label, Width = Dim.Percent(96), Height = Dim.Percent(92) };
        dialog.SetScheme(StudioPalette.Create());
        double? scale = TimeScale(entries, evidence.Elapsed);
        var list = new ListView { Width = Dim.Percent(45), Height = Dim.Fill(1) };
        list.SetSource<string>([.. entries.Select(e => InspectorRun.Clean($"{e.Bar(evidence.Events.Length, scale)} {(e.Concurrent ? "∥" : " ")} {e.Label}   {e.Call.Status}"))]);
        list.Value = 0;
        var details = new StudioTextView { X = Pos.Right(list), Width = Dim.Fill(), Height = Dim.Fill(1), ReadOnly = true, WordWrap = true, TabKeyAddsTab = false };
        void Draw() => details.Text = InspectorRun.Clean(entries[list.Value ?? 0].Details);
        list.ValueChanged += (_, _) => Draw();
        string axis = scale.HasValue ? $"Elapsed time 0–{scale / 1000:N2} s" : "Event order (timing unavailable)";
        string footer = axis + "   " + (evidence.Coverage.Length == 0 ? "" : evidence.Coverage + "   ") + "↑/↓ select invocation   ∥ overlap observed   Tab scroll details   Esc close";
        dialog.Add(list, details, new Label { Text = footer, Y = Pos.AnchorEnd(1) });
        Draw();
        StudioTerminal.App.Run(dialog);
    }
}
