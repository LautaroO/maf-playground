using System.Text.Json;

namespace MafPlayground.CLI.Tui;

internal static class StudioJson
{
    // Top-level HTTP contracts use camelCase. Embedded captured DTOs retain PascalCase.
    public static JsonElement Field(this JsonElement json, string name)
    {
        if (json.ValueKind == JsonValueKind.Object &&
            (json.TryGetProperty(name, out var value) || json.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value)))
            return value;
        return default;
    }

    public static string? Str(this JsonElement json, string name) =>
        json.Field(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static long Number(this JsonElement json, string name) =>
        json.Field(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : 0;

    public static JsonElement[] Items(this JsonElement json) =>
        json.ValueKind == JsonValueKind.Array ? [.. json.EnumerateArray()] : [];

    public static string Pretty(this JsonElement json) =>
        json.ValueKind == JsonValueKind.Undefined ? "Unavailable" : JsonSerializer.Serialize(json, InspectorRun.Json);

    public static string Label(this JsonElement json, string name) => InspectorRun.Clean(json.Str(name) ?? "—").Replace('\n', ' ');

    public static string RunStatus(this JsonElement run) => run.Number("status") switch
    {
        0 => "Queued",
        1 => "Running",
        2 => "Completed",
        3 => "Partially failed",
        4 => "Failed",
        5 => "Cancelled",
        6 => "Timed out",
        _ => "Unknown"
    };
}

internal sealed class RemoteJournal(string id)
{
    private const int MaxEvents = 4096;
    private const int MaxChars = 8 * 1024 * 1024;
    private readonly Queue<(JsonElement Event, int Chars)> _events = new();
    private int _chars;
    public string Id { get; } = id;
    public JsonElement Snapshot { get; private set; }
    public long Cursor { get; private set; }
    public bool HasGap { get; private set; }
    public IEnumerable<JsonElement> Events => _events.Select(e => e.Event);
    public bool Finished => Snapshot.Field("isTerminal").ValueKind == JsonValueKind.True && Cursor >= Snapshot.Number("latestSequence");

    public void Apply(JsonElement snapshot, JsonElement page)
    {
        if (snapshot.Str("runId") != Id) throw new InvalidDataException("Received a different Studio run.");
        Snapshot = snapshot;
        HasGap |= page.Field("hasGap").ValueKind == JsonValueKind.True;
        foreach (var item in page.Field("events").Items())
        {
            long sequence = item.Number("sequence");
            if (sequence <= Cursor) continue;
            if (item.Str("runId") != Id) throw new InvalidDataException("Received an event from a different run.");
            HasGap |= sequence != Cursor + 1;
            Cursor = sequence;
            int size = item.GetRawText().Length;
            _events.Enqueue((item, size));
            _chars += size;
            while (_events.Count > MaxEvents || _chars > MaxChars)
            {
                _chars -= _events.Dequeue().Chars;
                HasGap = true;
            }
        }
        Cursor = Math.Max(Cursor, page.Number("nextCursor"));
    }

    public static bool IsFailure(JsonElement item) => item.Str("kind") is "execution_error" or "model_failed" or "executor_failed"
        || item.Str("kind") == "span" && item.Field("data").Str("status") == "Error";

    public static IEnumerable<JsonElement> Failures(JsonElement[] events)
    {
        var cancelled = events.Where(e => e.Str("kind") is "executor_cancelled" or "model_cancelled").ToArray();
        return events.Where(IsFailure).Where(e => e.Str("kind") != "span" || !cancelled.Any(c =>
            c.Str("invocationId") is { } inv && inv == e.Str("invocationId") ||
            c.Str("spanId") is { } span && span == e.Field("data").Str("spanId")))
            .GroupBy(e => e.Str("invocationId") is { } invocation ? "invocation/" + invocation :
                (e.Str("spanId") ?? e.Field("data").Str("spanId")) is { } span ? "span/" + span : "event/" + e.Number("sequence"))
            .Select(g => g.MinBy(e => e.Str("kind") switch { "model_failed" => 0, "executor_failed" => 1, "execution_error" => 2, _ => 3 }));
    }

    public static string EventLabel(JsonElement item) =>
        $"#{item.Number("sequence")}  {item.Label("kind")}  {item.Label("executorId")}";

    public static string? PayloadId(JsonElement data) => data.Str("contentType") is not null ? data.Str("id") : null;

    public static Dictionary<string, string> NodeStates(IEnumerable<JsonElement> events)
    {
        Dictionary<(string Executor, string Invocation), string> lifecycle = [];
        foreach (var item in events)
        {
            string? state = item.Str("kind") switch
            {
                "executor_started" => "Active",
                "executor_completed" => "Completed",
                "executor_failed" => "Failed",
                "executor_cancelled" => "Cancelled",
                _ => null
            };
            if (state is not null && item.Str("executorId") is { } id && item.Str("invocationId") is { } invocation)
                lifecycle[(id, invocation)] = state;
        }
        return lifecycle.GroupBy(x => x.Key.Executor).ToDictionary(g => g.Key, g =>
            g.Any(x => x.Value == "Active") ? "Active" : g.Any(x => x.Value == "Failed") ? "Failed" :
            g.Any(x => x.Value == "Cancelled") ? "Cancelled" : "Completed");
    }

    public static string Graph(JsonElement topology, IReadOnlyDictionary<string, string> states)
    {
        string Decorate(string id) => states.GetValueOrDefault(id) switch
        {
            "Active" => "▶ " + id,
            "Failed" => "× " + id,
            "Cancelled" => "■ " + id,
            "Completed" => "✓ " + id,
            _ => "· " + id
        };
        return Graph(topology, Decorate);
    }

    public static string Graph(JsonElement topology, Func<string, string> decorate)
    {
        var nodes = topology.Field("nodes").Items().Select(n => n.Str("id")).OfType<string>().Distinct().ToArray();
        if (nodes.Length == 0) return topology.Str("unavailableReason") ?? "No declared topology.";
        var edges = topology.Field("edges").Items();
        // Large or highly connected graphs are more legible as an adjacency list.
        if (nodes.Length > 32 || edges.Length > 64 || nodes.Any(n => n.Length > 80) ||
            edges.Any(e => e.Field("sourceIds").GetArrayLength() * (long)e.Field("targetIds").GetArrayLength() > 64))
            return string.Join('\n', nodes.Select(decorate).Concat(edges.Select(edge => $"{Ends(edge, "sourceIds")} → {Ends(edge, "targetIds")}")));
        var connections = from e in edges
                          from a in e.Field("sourceIds").Items()
                          from b in e.Field("targetIds").Items()
                          where nodes.Contains(a.GetString()) && nodes.Contains(b.GetString())
                          select (a.GetString()!, b.GetString()!);
        return GraphEvidence.Render(topology.Str("startNodeId") ?? nodes[0], nodes, connections, decorate);
    }

    private static string Ends(JsonElement edge, string field) =>
        string.Join(", ", edge.Field(field).Items().Select(x => x.GetString()));

    private JsonElement[] ModelSpans() => Events.Where(e => e.Str("kind") == "span").Select(e => e.Field("data"))
        .Where(s => s.Str("source") == "MafPlayground.Studio.Model").DistinctBy(s => (s.Str("traceId"), s.Str("spanId"))).ToArray();

    public (long? Input, long? Output) ModelTokens()
    {
        if (HasGap || Snapshot.Field("captureComplete").ValueKind != JsonValueKind.True ||
            Events.Any(e => e.Number("captureState") != 0)) return (null, null);
        var spans = ModelSpans();
        long? Sum(string name)
        {
            if (spans.Length == 0) return null;
            long total = 0;
            foreach (var span in spans)
            {
                var field = span.Field(name);
                if (field.ValueKind != JsonValueKind.Number || !field.TryGetInt64(out var n) || n < 0 || n > long.MaxValue - total) return null;
                total += n;
            }
            return total;
        }
        return (Sum("inputTokens"), Sum("outputTokens"));
    }

    public string Metrics()
    {
        var spans = ModelSpans();
        string Sum(string name) => spans.Length > 0 && spans.All(s => s.Field(name).ValueKind == JsonValueKind.Number)
            ? spans.Sum(s => s.Field(name).GetDouble()).ToString("G") : "unavailable";
        var currencies = spans.Select(s => s.Str("currency")).Distinct().ToArray();
        string cost = currencies is [string currency] ? $"{Sum("cost")} {currency}" : "unavailable";
        return $"Observed model attempts  {spans.Length}\nInput tokens  {Sum("inputTokens")}\nOutput tokens  {Sum("outputTokens")}\nCost  {cost}";
    }
}
