using System.Text.Json;
using Terminal.Gui;

namespace MafPlayground.CLI.Tui;

internal sealed partial class RemoteWorkspace
{
    private enum Panel { Graph, Events, Spans, Failures, Result, Metrics }
    private sealed record Row(string Key, string Title, Func<string> Details, Action? Open = null);
    private Panel _panel;
    private Row[] _rows = [];
    private bool _updating;

    private void ViewPanel(Panel panel)
    {
        _panel = panel;
        _rows = [];
        Refresh();
    }

    private void Refresh()
    {
        string? selected = _rows.ElementAtOrDefault(_list.SelectedItem)?.Key;
        JsonElement snapshot = _run?.Snapshot ?? default;
        JsonElement topology = snapshot.Field("topology");
        if (topology.ValueKind != JsonValueKind.Object && _run is null) topology = _draft.Topology;
        JsonElement request = snapshot.Field("request");
        string scope = _run is null ? $"Draft  {_draft.Id} / {_draft.Profile}" :
            $"{snapshot.RunStatus()}  {request.Label("entityId")} / {request.Label("profileId")}  ·  {_run.Id}";
        _header.Text = InspectorRun.Clean($"{_panel}   |   {scope}   |   session {(request.Str("sessionId") ?? (_run is null ? _session : null) ?? "—")}" +
            (_run?.HasGap == true || snapshot.Field("captureComplete").ValueKind == JsonValueKind.False ? "   |   incomplete capture" : ""));
        _draftLabel.Text = InspectorRun.Clean($"Next run   {_draft.Id} / {_draft.Profile}   |   session {_session ?? "—"}");
        JsonElement[] events = _run?.Events.ToArray() ?? [];
        var rows = _panel switch
        {
            Panel.Graph => GraphRows(topology, events),
            Panel.Events => events.Select(EventRow),
            Panel.Spans => SpanRows(events),
            Panel.Failures => RemoteJournal.Failures(events).Select(EventRow),
            Panel.Result => ResultRows(snapshot),
            Panel.Metrics => [new Row("metrics", "Observed model usage", () => _run?.Metrics() ?? "Open a run to inspect model usage.")],
            _ => []
        };
        var next = rows.ToArray();
        if (next.Length == 0)
        {
            string info = _panel == Panel.Failures && snapshot.Str("errorCategory") is { } error
                ? $"Run error: {error}. No detailed failure event was retained." : "No retained entries.";
            next = [new("empty", info, () => info)];
        }
        _updating = true;
        try
        {
            bool sameLabels = _rows.Select(r => r.Title).SequenceEqual(next.Select(r => r.Title));
            _rows = next;
            if (!sameLabels) _list.SetSource(_rows.Select(r => InspectorRun.Clean(r.Title).Replace('\n', ' ')).ToArray());
            _list.SelectedItem = Math.Max(0, Array.FindIndex(_rows, r => r.Key == selected));
        }
        finally { _updating = false; }
        ShowSelection();
        Notice(_message);
    }

    private void ShowSelection()
    {
        if (_updating || _rows.ElementAtOrDefault(_list.SelectedItem) is not { } row) return;
        string body = InspectorRun.Clean(row.Details());
        if (_detail.Text.ToString() != body) _detail.Text = body;
    }

    private void InspectSelection()
    {
        if (_rows.ElementAtOrDefault(_list.SelectedItem) is not { } row) return;
        if (row.Open is null) Show("Inspect · " + InspectorRun.Clean(row.Title), row.Details());
        else Guard(row.Open);
    }

    private IEnumerable<Row> GraphRows(JsonElement topology, JsonElement[] events)
    {
        var nodes = topology.Field("nodes").Items();
        var states = RemoteJournal.NodeStates(events);
        var diagram = new Lazy<string>(() => RemoteJournal.Graph(topology, states));
        if (nodes.Length == 0)
        {
            var calls = SpanRows(events).ToArray();
            var tools = events.Where(e => e.Str("kind") is "tool_arguments" or "tool_result").Select(EventRow).ToArray();
            yield return new("graph", "Observed calls", () => diagram.Value + "\n\n" +
                string.Join('\n', calls.Concat(tools).Select(row => row.Title)));
            foreach (var row in calls.Concat(tools)) yield return row;
            yield break;
        }
        yield return new("graph", "Workflow topology", () => diagram.Value + "\n\n▶ Active    ✓ Completed    × Failed    ■ Cancelled");
        foreach (var node in nodes)
        {
            var id = node.Str("id")!;
            var observations = events.Where(e => e.Str("executorId") == id).ToArray();
            string label = node.Str("label") ?? id;
            var state = states.GetValueOrDefault(id, "Unobserved");
            yield return new("node-" + id, $"{label}  {state}", () =>
                $"{diagram.Value}\n\n{label}  ·  {state}\n\n" + string.Join('\n', observations.Select(RemoteJournal.EventLabel)),
                () => InspectNode(id, topology, observations));
        }
        foreach (var edge in topology.Field("edges").Items())
            yield return new("edge-" + edge.Str("id"), "→ " + (edge.Str("label") ?? edge.Label("kind")), () => edge.Pretty());
    }

    private static IEnumerable<Row> ResultRows(JsonElement snapshot)
    {
        if (snapshot.ValueKind == JsonValueKind.Undefined) yield break;
        yield return new("result", "Output · " + snapshot.RunStatus(), () => snapshot.Field("result").Pretty());
        yield return new("input", "Captured request", () => snapshot.Field("request").Pretty());
        yield return new("run", "Execution details", () => JsonSerializer.Serialize(new
        {
            status = snapshot.RunStatus(),
            errorCategory = snapshot.Str("errorCategory"),
            outcome = snapshot.Field("outcome").ToString(),
            startedAt = snapshot.Str("startedAt"),
            completedAt = snapshot.Str("completedAt"),
            capturedBytes = snapshot.Number("capturedBytes"),
            events = snapshot.Number("totalEvents"),
            profile = snapshot.Field("profile").ToString(),
            provenance = snapshot.Field("provenance").ToString()
        }, InspectorRun.Json));
    }

    private void InspectNode(string id, JsonElement topology, JsonElement[] observations)
    {
        var invocations = observations.GroupBy(e => e.Str("invocationId")).ToArray();
        var edges = topology.Field("edges").Items();
        var neighbors = (from edge in edges
                         from direction in new[] { (From: "sourceIds", To: "targetIds", Label: "Outgoing → "),
                             (From: "targetIds", To: "sourceIds", Label: "Incoming ← ") }
                         where edge.Field(direction.From).Items().Any(n => n.GetString() == id)
                         from target in edge.Field(direction.To).Items()
                         select (direction.Label, Node: target.GetString())).Distinct().ToArray();
        var choices = invocations.Select((g, index) => g.Key is null ? $"Uncorrelated events  ({g.Count()})" :
            $"Invocation {index + 1}   {g.Key}   ({g.Count()} events)")
            .Concat(neighbors.Select(n => n.Label + n.Node)).ToArray();
        int index = Pick("Executor · " + id, choices);
        if (index < 0) return;
        if (index < invocations.Length) PickObservation("Invocation · " + (invocations[index].Key ?? "uncorrelated"), invocations[index].ToArray());
        else
        {
            int row = Array.FindIndex(_rows, r => r.Key == "node-" + neighbors[index - invocations.Length].Node);
            if (row >= 0) _list.SelectedItem = row;
        }
    }

    private Row EventRow(JsonElement item) =>
        new("event-" + item.Number("sequence"), RemoteJournal.EventLabel(item), () => item.Pretty(), () => InspectEvent(item));

    private void PickObservation(string title, JsonElement[] observations)
    {
        int i = Pick(title, observations.Select(RemoteJournal.EventLabel).ToArray());
        if (i >= 0) InspectEvent(observations[i]);
    }

    private IEnumerable<Row> SpanRows(JsonElement[] events)
    {
        var spans = events.Where(e => e.Str("kind") == "span").Select(e => e.Field("data"))
            .Where(s => s.Str("spanId") != null).DistinctBy(s => (s.Str("traceId"), s.Str("spanId")))
            .OrderBy(s => s.Str("startedAt")).ToArray();
        var byId = spans.ToDictionary(s => (s.Str("traceId"), s.Str("spanId")));
        foreach (var span in spans)
        {
            var current = span;
            HashSet<string> visited = [];
            while (visited.Count < 16 && current.Str("parentSpanId") is { } parent &&
                   byId.TryGetValue((span.Str("traceId"), parent), out var ancestor) && visited.Add(parent))
                current = ancestor;
            int depth = visited.Count;
            string name = new string(' ', depth * 2) + (depth == 0 ? "" : "└ ") + span.Label("name");
            yield return new("span-" + span.Str("spanId"), name + "  " + span.Label("duration"),
                () => $"{span.Label("name")}\nStatus   {span.Label("status")}\nSource   {span.Label("source")}\nDuration   {span.Label("duration")}\n" +
                    $"Trace   {span.Label("traceId")}\nSpan   {span.Label("spanId")}\nParent   {span.Label("parentSpanId")}\n" +
                    $"Executor   {span.Label("executorId")}\nInvocation   {span.Label("invocationId")}\n\nEnter to load retained span or payload.",
                () => InspectSpan(span.Str("spanId")!));
        }
    }

    private void InspectEvent(JsonElement item)
    {
        if (_run is null) return;
        var data = item.Field("data");
        if (item.Str("kind") == "span" && data.Str("spanId") is { } spanId) InspectSpan(spanId);
        else if (RemoteJournal.PayloadId(data) is { } payloadId) LoadPayload(_run.Id, payloadId);
        else Show(RemoteJournal.EventLabel(item), item.Pretty());
    }

    private void InspectSpan(string spanId)
    {
        if (_run is not { } run) return;
        Request(() => _client.SpanAsync(run.Id, spanId, _token), span =>
        {
            string[] names = ["attributes", "events", "links"];
            var payloads = names.Where(name => RemoteJournal.PayloadId(span.Field(name)) is not null).ToArray();
            int i = Pick("Span · " + span.Label("name"), ["Full span", .. payloads]);
            if (i == 0) Show("Span · " + span.Label("name"), span.Pretty());
            else if (i > 0) LoadPayload(run.Id, span.Field(payloads[i - 1]).Str("id")!);
        });
    }

    private void LoadPayload(string runId, string id) =>
        Request(() => _client.PayloadAsync(runId, id, _token), payload => Show("Retained payload · " + id, payload.Pretty()));
}
