using System.Text.Json;

namespace MafPlayground.CLI.Tui;

internal sealed record CapturedEdge(string From, string To);
internal sealed record EventLink(int Event, string Node, int Invocation);
internal sealed record LocalCapture(
    int Version, string Entity, string? Model, string Input, DateTimeOffset Started, string Status, TimeSpan? Elapsed,
    string Result, string StartNode, string[] Nodes, CapturedEdge[] Edges, string[] Tools,
    Dictionary<string, List<Invocation>> Invocations, string[] Events, EventLink[] Links, long? InputTokens = null, long? OutputTokens = null)
{
    private const int MaximumBytes = 8 * 1024 * 1024;

    public WorkflowGraph Graph() => new(StartNode, Nodes, Edges.Select(edge => (edge.From, edge.To)));

    public void Save(string path)
    {
        if (!IsSupported()) throw new InvalidDataException("This run exceeds the capture format limits or is still active.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(this, InspectorRun.Json);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Capture exceeds 8 MiB.");
        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".maf-{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var stream = new FileStream(temporary, options)) stream.Write(bytes);
            File.Move(temporary, path);
        }
        finally { File.Delete(temporary); }
    }

    public static LocalCapture Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Capture exceeds 8 MiB.");
        LocalCapture? capture = JsonSerializer.Deserialize<LocalCapture>(stream, InspectorRun.Json);
        if (capture is null || !capture.IsSupported()) throw new InvalidDataException("Invalid or unsupported TUI capture.");
        return capture;
    }

    private bool IsSupported()
    {
        if (Version != 1 || InputTokens < 0 || OutputTokens < 0 || string.IsNullOrWhiteSpace(Entity) || Entity.Length > 128 || Entity.Any(char.IsControl) || Model is not null && (!AIModelSelection.TryParse(Model, out _) || Model.Any(char.IsControl)) || Input is null || Result is null ||
            Status is not ("Completed" or "Failed" or "Partial result" or "Cancelled") ||
            Nodes is not { Length: > 0 and <= 128 } || Edges is not { Length: <= 512 } ||
            Tools is null || Invocations is null || Events is null || Links is null ||
            !DisplayText(Result) || Input.Any(c => char.IsControl(c) && !char.IsWhiteSpace(c))) return false;
        var ids = Nodes.ToHashSet(StringComparer.Ordinal);
        return ids.Count == Nodes.Length && ids.Contains(StartNode) &&
            Nodes.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && !id.Any(char.IsControl)) &&
            Tools.All(ids.Contains) && Edges.All(e => e is not null && ids.Contains(e.From) && ids.Contains(e.To)) &&
            Invocations.All(p => ids.Contains(p.Key) && p.Value is not null && p.Value.All(i =>
                i is not null && DisplayText(i.Input) && DisplayText(i.Output) && DisplayText(i.Error) && i.Finished)) &&
            Events.All(DisplayText) &&
            Links.All(l => l is not null && l.Event >= 0 && l.Event < Events.Length && l.Node is not null &&
                Invocations.TryGetValue(l.Node, out var attempts) && l.Invocation >= 0 && l.Invocation < attempts.Count);
    }

    private static bool DisplayText(string? text) =>
        text is { Length: <= 65550 } && text.All(c => c == '\n' || !char.IsControl(c));

    public static string Compare(LocalCapture baseline, LocalCapture candidate)
    {
        List<string> lines = [$"{baseline.Entity} → {candidate.Entity}",
            $"Model    {baseline.Model} → {candidate.Model}", $"Status   {baseline.Status} → {candidate.Status}", $"Time     {baseline.Elapsed:g} → {candidate.Elapsed:g}", ""];
        foreach (string id in baseline.Nodes.Union(candidate.Nodes))
        {
            int before = baseline.Invocations.GetValueOrDefault(id)?.Count ?? 0;
            int after = candidate.Invocations.GetValueOrDefault(id)?.Count ?? 0;
            if (before != after) lines.Add($"{id}: {before} → {after} invocations");
        }
        foreach (var (label, before, after) in new[] { ("Input", baseline.Input, candidate.Input), ("Result", baseline.Result, candidate.Result) })
        {
            int first = lines.Count;
            if (before != after)
            {
                try
                {
                    using var left = JsonDocument.Parse(before);
                    using var right = JsonDocument.Parse(after);
                    CompareJson(label, left.RootElement, right.RootElement, lines, first + 48);
                }
                catch (JsonException) { lines.Add(Difference(label, before, after)); }
            }
            if (lines.Count == first) lines.Add($"{label} unchanged");
            if (lines.Count == first + 48) lines.Add($"Showing up to 48 {label.ToLowerInvariant()} differences.");
        }
        return InspectorRun.Clean(string.Join('\n', lines));
    }
    private static void CompareJson(string path, JsonElement left, JsonElement right, List<string> lines, int limit)
    {
        if (lines.Count >= limit || JsonElement.DeepEquals(left, right)) return;
        if (left.ValueKind == JsonValueKind.Object && right.ValueKind == JsonValueKind.Object)
        {
            var names = left.EnumerateObject().Concat(right.EnumerateObject()).Select(p => p.Name).Distinct();
            foreach (string name in names)
            {
                if (lines.Count >= limit) break;
                bool existed = left.TryGetProperty(name, out var a);
                bool exists = right.TryGetProperty(name, out var b);
                string child = path + "." + InspectorRun.Clean(name[..Math.Min(name.Length, 80)]);
                if (existed && exists) CompareJson(child, a, b, lines, limit);
                else lines.Add(Difference(child, existed ? a.ToString() : "(absent)", exists ? b.ToString() : "(absent)"));
            }
        }
        else if (left.ValueKind == JsonValueKind.Array && right.ValueKind == JsonValueKind.Array)
        {
            int before = left.GetArrayLength(), after = right.GetArrayLength();
            if (before != after) lines.Add($"{path}.length: {before} → {after}");
            using var a = left.EnumerateArray();
            using var b = right.EnumerateArray();
            int index = 0;
            while (lines.Count < limit && a.MoveNext() && b.MoveNext())
                CompareJson($"{path}[{index++}]", a.Current, b.Current, lines, limit);
        }
        else lines.Add(Difference(path, left.GetRawText(), right.GetRawText()));
    }

    private static string Difference(string path, string before, string after)
    {
        int offset = 0;
        while (offset < before.Length && offset < after.Length && before[offset] == after[offset]) offset++;
        int start = Math.Max(0, offset - 32);
        string Excerpt(string value) => (start > 0 ? "…" : "") +
            value.Substring(start, Math.Min(140, value.Length - start)) + (value.Length - start > 140 ? "…" : "");
        return $"{path}\n  − {Excerpt(before)}\n  + {Excerpt(after)}";
    }

}

internal sealed partial class InspectorRun
{
    public LocalCapture Capture(string entity, string? model, string input, DateTimeOffset started, WorkflowGraph graph) =>
        new(1, entity, model, input, started, Status, Elapsed, Result, graph.Nodes[0].Id, graph.Nodes.Select(n => n.Id).ToArray(),
            graph.Nodes.SelectMany(n => graph.Neighbors(n.Id, true).Select(next => new CapturedEdge(n.Id, next))).ToArray(),
            ToolNodes.ToArray(), new(_attempts), Events.ToArray(),
            Enumerable.Range(0, Events.Count).Where(i => EventTarget(i).HasValue).Select(i =>
            {
                var target = EventTarget(i)!.Value;
                return new EventLink(i, target.Id, target.Index);
            }).ToArray(), InputTokens, OutputTokens);

    public static InspectorRun Restore(LocalCapture capture)
    {
        var run = new InspectorRun
        {
            Status = capture.Status,
            Result = Clean(capture.Result),
            _savedElapsed = capture.Elapsed,
            InputTokens = capture.InputTokens,
            OutputTokens = capture.OutputTokens
        };
        foreach (var (node, attempts) in capture.Invocations) run._attempts.Add(node, attempts);
        run.Events.AddRange(capture.Events.Select(Clean));
        run._toolNodes.AddRange(capture.Tools);
        foreach (EventLink link in capture.Links) run._eventTargets[link.Event] = (link.Node, link.Invocation);
        return run;
    }
}
