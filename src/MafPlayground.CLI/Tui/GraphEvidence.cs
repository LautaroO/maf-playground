using System.Text.Json;

namespace MafPlayground.CLI.Tui;

internal sealed record GraphObservation(string Label, string? Node, string? InvocationId, string? State, string Detail);
internal sealed record GraphInvocation(string Id, string Status, string Input, string Output, string Error, TimeSpan? Duration = null, TimeSpan? StartOffset = null)
{
    public bool SameEvidence(GraphInvocation other) =>
        Status == other.Status && Input == other.Input && Output == other.Output && Error == other.Error;
}

internal sealed class GraphEvidence
{
    public required string Label { get; init; }
    public required string[] Nodes { get; init; }
    public required GraphObservation[] Events { get; init; }
    public required Func<Func<string, string>, string> Diagram { get; init; }
    public required Dictionary<string, GraphInvocation[]> Calls { get; init; }
    public string? RunNode { get; init; }
    public TimeSpan? Elapsed { get; init; }
    public string Coverage { get; init; } = "";

    public string ReplayFrame(int index)
    {
        var states = new Dictionary<(string Node, string Invocation), string>();
        foreach (var item in Events.Take(index + 1))
            if (item is { Node: { } node, InvocationId: { } invocation, State: { } state })
                states[(node, invocation)] = state;

        var markers = states.GroupBy(pair => pair.Key.Node).ToDictionary(group => group.Key, group =>
            group.Any(pair => pair.Value == "Active") ? "▶" : group.Any(pair => pair.Value == "Failed") ? "×" :
            group.Any(pair => pair.Value == "Cancelled") ? "■" : "✓");
        var selected = Events[index];
        return $"{Coverage}\n{Diagram(id => markers.GetValueOrDefault(id, "·") + " " + id)}\n\n{selected.Label}\n{selected.Detail}";
    }

    public static GraphEvidence From(LocalCapture capture)
    {
        var links = capture.Links.ToLookup(link => link.Event);
        return new()
        {
            Elapsed = capture.Elapsed,
            Label = $"{capture.Entity} · {capture.Started:g}",
            Nodes = capture.Nodes,
            Diagram = decorate => Render(capture.StartNode, capture.Nodes, capture.Edges.Select(e => (e.From, e.To)), decorate),
            Calls = capture.Invocations.ToDictionary(pair => pair.Key, pair => pair.Value.Select((call, i) =>
                new GraphInvocation(call.CallId ?? (i + 1).ToString(), call.Rejected ? "Rejected" : call.Failed ? "Failed" :
                    call.Finished ? "Completed" : "Active", call.Input, call.Output, call.Error, call.Duration, call.StartOffset)).ToArray()),
            Events = capture.Events.Select((label, index) =>
            {
                var link = links[index].LastOrDefault();
                var call = link is null ? null : capture.Invocations[link.Node][link.Invocation];
                bool validation = label.EndsWith("  ·  rejected", StringComparison.Ordinal);
                string? state = label.FirstOrDefault() switch
                {
                    '▶' => "Active",
                    '✓' => "Completed",
                    '×' when validation => null,
                    '×' when call?.Error == "Cancelled" && capture.Status == "Cancelled" => "Cancelled",
                    '×' => "Failed",
                    _ => null
                };
                // The archive stores final invocation payloads. Reveal final output only at its closing event.
                string detail = call is null ? "" : state == "Active" ? call.Input :
                    validation ? "Validation rejected" :
                    call.Error.Length > 0 ? call.Error : call.Output;
                return new GraphObservation(label, link?.Node, link is null ? null : call?.CallId ?? (link.Invocation + 1).ToString(), state, detail);
            }).ToArray()
        };
    }

    public static GraphEvidence From(RemoteJournal journal)
    {
        var snapshot = journal.Snapshot;
        var topology = snapshot.Field("topology");
        var events = journal.Events.ToArray();
        var nodes = topology.Field("nodes").Items().Select(node => node.Str("id")).OfType<string>()
            .Union(events.Select(item => item.Str("executorId")).OfType<string>()).ToArray();
        var startedAt = Timestamp(snapshot, "startedAt");
        var origin = startedAt ?? events.Select(e => Timestamp(e, "timestamp")).FirstOrDefault(t => t.HasValue);
        // Keep conflicting invocation correlations out of executor drilldowns, as Studio's comparer does.
        var calls = events.Where(e => e.Str("invocationId") is not null).GroupBy(e => e.Str("invocationId")!)
            .Where(group => group.First().Str("executorId") is not null && group.All(e => e.Str("executorId") == group.First().Str("executorId")))
            .GroupBy(group => group.First().Str("executorId")!).ToDictionary(group => group.Key, group =>
                group.Select(call => new GraphInvocation(call.Key, call.Select(State).LastOrDefault(state => state is not null) ?? "Unobserved",
                    Values(call, "executor_input"), Values(call, "executor_output"), Values(call, "executor_failed", "model_failed", "execution_error"), Duration(call), Timestamp(call.FirstOrDefault(e => State(e) == "Active"), "timestamp") - origin)).ToArray());
        string? runNode = null;
        if (nodes.Length == 0 && snapshot.Field("request").Str("entityId") is { } entityId)
        {
            runNode = "Run · " + entityId;
            nodes = [runNode];
            calls.Add(runNode, [new(journal.Id, snapshot.RunStatus(), snapshot.Field("request").Field("input").Pretty(),
                snapshot.Field("result").Pretty(), snapshot.Str("errorCategory") ?? "(no retained error)",
                Timestamp(snapshot, "completedAt") - startedAt, startedAt.HasValue ? TimeSpan.Zero : null)]);
        }
        var declared = topology.Field("nodes").Items().Select(n => n.Str("id")).OfType<string>().ToHashSet();
        return new()
        {
            Elapsed = Timestamp(snapshot, "completedAt") - origin,
            Label = $"{snapshot.Field("request").Label("entityId")} · {journal.Id}",
            Nodes = nodes,
            Calls = calls,
            RunNode = runNode,
            Coverage = journal.HasGap || snapshot.Field("captureComplete").ValueKind == JsonValueKind.False ||
                journal.Cursor < snapshot.Number("latestSequence") ? "Partial retained journal" : "",
            Diagram = decorate => RemoteJournal.Graph(topology, decorate) + string.Concat(nodes.Where(n => !declared.Contains(n)).Select(n => "\n" + decorate(n))),
            Events = events.Select(e => new GraphObservation(RemoteJournal.EventLabel(e), e.Str("executorId") ?? runNode,
                e.Str("invocationId") ?? (runNode is null ? null : journal.Id), runNode is null ? State(e) : e.Str("kind") switch
                {
                    "running" => "Active",
                    "Completed" => "Completed",
                    "Failed" or "PartiallyFailed" or "TimedOut" => "Failed",
                    "Cancelled" => "Cancelled",
                    _ => null
                }, Payload(e))).ToArray()
        };
    }

    private static DateTimeOffset? Timestamp(JsonElement item, string field) =>
        item.Field(field) is { ValueKind: JsonValueKind.String } value && value.TryGetDateTimeOffset(out var time) ? time : null;

    private static TimeSpan? Duration(IEnumerable<JsonElement> events)
    {
        var start = events.FirstOrDefault(e => State(e) == "Active");
        var finish = events.LastOrDefault(e => State(e) is "Completed" or "Failed" or "Cancelled");
        TimeSpan? elapsed = Timestamp(finish, "timestamp") - Timestamp(start, "timestamp");
        return elapsed >= TimeSpan.Zero ? elapsed : null;
    }

    private static string? State(JsonElement item) => item.Str("kind") switch
    {
        "executor_started" => "Active",
        "executor_completed" => "Completed",
        "executor_failed" => "Failed",
        "executor_cancelled" => "Cancelled",
        _ => null
    };

    private static string Values(IEnumerable<JsonElement> events, params string[] kinds)
    {
        string[] values = events.Where(e => kinds.Contains(e.Str("kind"))).Select(Payload).ToArray();
        return values.Length == 0 ? "(no retained observation)" : string.Join("\n", values);
    }

    private static string Payload(JsonElement item)
    {
        var data = item.Field("data");
        var value = data.Field("value");
        string state = item.Number("captureState") switch { 1 => "[redacted] ", 2 => "[truncated] ", 3 => "[unavailable] ", _ => "" };
        return state + (value.ValueKind == JsonValueKind.Undefined ? data : value).Pretty();
    }

    internal static string Render(string start, string[] nodes, IEnumerable<(string From, string To)> edges, Func<string, string> decorate)
    {
        // Layout on stable IDs so changes in state never reorder parallel nodes.
        var graph = new WorkflowGraph("  " + start, nodes.Select(id => "  " + id), edges.Select(e => ("  " + e.From, "  " + e.To)));
        char[][] lines = graph.Lines.Select(line => line.ToCharArray()).ToArray();
        foreach (var node in graph.Nodes)
        {
            string label = decorate(node.Id[2..]);
            label.CopyTo(0, lines[node.Y], node.X, label.Length);
        }
        return string.Join('\n', lines.Select(line => new string(line).TrimEnd()));
    }
}
