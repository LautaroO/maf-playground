using System.Text.Json;
using MafPlayground.Studio.Contracts;

namespace MafPlayground.Studio.Comparison;

/// <summary>Deterministic comparison of sanitized retained evidence, never a semantic quality evaluation.</summary>
public sealed class StudioRunComparer
{
    internal const int MaxEvents = 2048;
    private const int MaxChanges = 48;
    private const int MaxInvocations = 64;
    private const int MaxPreview = 160;
    private const int MaxResponseBytes = 96 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public StudioComparison Compare(StudioRunSnapshot baseline, StudioRunSnapshot candidate,
        IReadOnlyList<StudioEvent> baselineEvents, IReadOnlyList<StudioEvent> candidateEvents,
        bool baselineGap = false, bool candidateGap = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (baseline.HostInstanceId != candidate.HostInstanceId) throw new ArgumentException("Runs must belong to the same host lifetime.");
        if (!baseline.IsTerminal || !candidate.IsTerminal) throw new InvalidOperationException("Comparison requires terminal runs.");
        baselineGap |= baseline.EarliestSequence > 1 || baselineEvents.Count > MaxEvents;
        candidateGap |= candidate.EarliestSequence > 1 || candidateEvents.Count > MaxEvents;
        var aEvents = baselineEvents.Take(MaxEvents).ToArray();
        var bEvents = candidateEvents.Take(MaxEvents).ToArray();
        var a = Summarize(baseline, aEvents, baselineGap, cancellationToken);
        var b = Summarize(candidate, bEvents, candidateGap, cancellationToken);
        var diff = new Difference(cancellationToken);
        diff.Visit(baseline.Request.Input, candidate.Request.Input, "/input");
        diff.Visit(baseline.Result, candidate.Result, "/result");
        diff.Visit(JsonSerializer.SerializeToElement(baseline.Request.ProfileId), JsonSerializer.SerializeToElement(candidate.Request.ProfileId), "/profileId");
        diff.Visit(JsonSerializer.SerializeToElement(baseline.Profile, Json), JsonSerializer.SerializeToElement(candidate.Profile, Json), "/profile");
        List<string> limitations = ["Structural/text differences do not establish semantic correctness or quality.",
            "Model attempts are Studio observations, including attempts rejected before provider execution; not billed-call counts.",
            "Usage and cost aggregate only unique Studio model spans; missing/partial coverage is unknown. Native and agent spans are excluded."];
        if (!a.CaptureComplete || !b.CaptureComplete || a.JournalHasGap || b.JournalHasGap || a.HasRedactions || b.HasRedactions)
            limitations.Add("Capture is incomplete or redacted, or retained journal history has gaps. Metrics and differences describe only available evidence.");
        if (diff.PreviewTruncated) limitations.Add("Value/path previews are capped at 160 characters; text previews begin at the first changed UTF-16 character (textOffset). Full retained values remain in run inspection.");
        var allMatches = Match(aEvents, bEvents).ToArray();
        bool eventLimit = baselineEvents.Count > MaxEvents || candidateEvents.Count > MaxEvents ||
            (aEvents.Length == MaxEvents && aEvents[^1].Sequence < baseline.LatestSequence) ||
            (bEvents.Length == MaxEvents && bEvents[^1].Sequence < candidate.LatestSequence);
        bool truncated = diff.Truncated || allMatches.Length > MaxInvocations || eventLimit;
        if (truncated) limitations.Add("Comparison limits: 48 changes, 64 invocation rows, 8192 visited JSON nodes, 2048 properties per object, depth 32 and 2048 events per run. Duplicate object properties are ambiguous and mark the diff truncated.");
        var result = new StudioComparison(baseline.HostInstanceId, a, b, diff.Changes,
            Delta(Duration(baseline), Duration(candidate)), Delta(a.Metrics.ObservedModelAttempts, b.Metrics.ObservedModelAttempts),
            Delta(a.Metrics.InputTokens, b.Metrics.InputTokens), Delta(a.Metrics.OutputTokens, b.Metrics.OutputTokens),
            new(a.Metrics.Cost, b.Metrics.Cost, a.Metrics.Currency is not null && a.Metrics.Currency == b.Metrics.Currency ? Subtract(a.Metrics.Cost, b.Metrics.Cost) : null),
            "Exact executor ID, then invocation occurrence in journal sequence order; repeated events are grouped by invocation ID. This is not causal equivalence. Missing/conflicting executor correlation stays unmatched; IDs are preserved.",
            allMatches.Take(MaxInvocations).ToArray(), truncated, limitations);
        // Both transports consume this exact bounded result, even for adversarial escaped identifiers.
        while (JsonSerializer.SerializeToUtf8Bytes(result, Json).Length > MaxResponseBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!limitations.Contains("Response trimmed to 96 KiB.")) limitations.Add("Response trimmed to 96 KiB.");
            if (result.Invocations.Count > 0) result = result with { Invocations = result.Invocations.SkipLast(1).ToArray(), Truncated = true };
            else if (result.Changes.Count > 0) result = result with { Changes = result.Changes.SkipLast(1).ToArray(), Truncated = true };
            else throw new ArgumentException("Comparison metadata exceeds response limit.");
        }
        return result;
    }

    private static StudioComparisonRun Summarize(StudioRunSnapshot run, StudioEvent[] events, bool gap, CancellationToken token)
    {
        var spans = new Dictionary<(string, string), StudioSpan>();
        bool complete = run.CaptureComplete;
        bool redacted = false;
        foreach (var item in events)
        {
            token.ThrowIfCancellationRequested();
            if (item.CaptureState is StudioCaptureState.Truncated or StudioCaptureState.Unavailable) complete = false;
            redacted |= item.CaptureState == StudioCaptureState.Redacted;
            if (item.Kind != "span") continue;
            try
            {
                var span = item.Data.Deserialize<StudioSpan>();
                if (span is null || string.IsNullOrEmpty(span.SpanId) || string.IsNullOrEmpty(span.TraceId) || string.IsNullOrEmpty(span.Source)) { gap = true; continue; }
                if (span.Source == "MafPlayground.Studio.Model") spans[(span.TraceId, span.SpanId)] = span;
            }
            catch (JsonException) { gap = true; }
        }
        var model = spans.Values.ToArray();
        int inputs = model.Count(x => x.InputTokens >= 0), outputs = model.Count(x => x.OutputTokens >= 0);
        int costs = model.Count(x => x.Cost is >= 0 && double.IsFinite(x.Cost.Value) && !string.IsNullOrWhiteSpace(x.Currency));
        string? currency = model.Select(x => x.Currency).Distinct(StringComparer.Ordinal).Count() == 1 ? model[0].Currency : null;
        bool known = complete && !gap && !redacted && model.Length > 0;
        var metrics = new StudioComparisonMetrics(model.Length,
            known && inputs == model.Length ? model.Sum(x => (double)x.InputTokens!.Value) : null,
            known && outputs == model.Length ? model.Sum(x => (double)x.OutputTokens!.Value) : null,
            known && costs == model.Length && currency is not null ? Finite(model.Sum(x => x.Cost!.Value)) : null,
            currency, inputs, outputs, costs);
        return new(run.RunId, run.Request.EntityId, run.Request.ProfileId, run.Status, run.Outcome, run.Profile, complete, gap, redacted, metrics);
    }
    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
    private static double? Duration(StudioRunSnapshot run) => run.StartedAt is { } start && run.CompletedAt is { } end ? (end - start).TotalMilliseconds : null;
    private static double? Subtract(double? a, double? b) => a is { } x && b is { } y ? Finite(y - x) : null;
    private static StudioMetricDelta Delta(double? a, double? b) => new(a, b, Subtract(a, b));
    private static IEnumerable<StudioInvocationMatch> Match(StudioEvent[] a, StudioEvent[] b)
    {
        static (string Id, string? Executor)[] Project(StudioEvent[] events) => events.OrderBy(x => x.Sequence)
            .Where(x => !string.IsNullOrEmpty(x.InvocationId)).GroupBy(x => x.InvocationId!, StringComparer.Ordinal)
            .Select(group => (group.Key, group.Select(x => x.ExecutorId).Distinct(StringComparer.Ordinal).Count() == 1 ? group.First().ExecutorId : null)).ToArray();
        var baseline = Project(a);
        var candidate = Project(b);
        var candidatesByExecutor = candidate.Where(x => x.Executor is not null).GroupBy(x => x.Executor!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in baseline)
        {
            int occurrence = item.Executor is null ? 1 : counts.GetValueOrDefault(item.Executor) + 1;
            if (item.Executor is not null) counts[item.Executor] = occurrence;
            var other = item.Executor is not null && candidatesByExecutor.TryGetValue(item.Executor, out var peers) && occurrence <= peers.Length ? peers[occurrence - 1] : default;
            if (other.Id is not null) used.Add(other.Id);
            yield return new(item.Executor, occurrence, item.Id, other.Id);
        }
        counts.Clear();
        foreach (var item in candidate)
        {
            int occurrence = item.Executor is null ? 1 : counts.GetValueOrDefault(item.Executor) + 1;
            if (item.Executor is not null) counts[item.Executor] = occurrence;
            if (!used.Contains(item.Id)) yield return new(item.Executor, occurrence, null, item.Id);
        }
    }

    private sealed class Difference(CancellationToken token)
    {
        private int visited;
        public List<StudioValueChange> Changes { get; } = [];
        public bool Truncated { get; private set; }
        public bool PreviewTruncated { get; private set; }
        public void Visit(JsonElement? a, JsonElement? b, string path, int depth = 0, bool captureRoot = true)
        {
            token.ThrowIfCancellationRequested();
            if (++visited > 8192 || depth > 32 || Changes.Count >= MaxChanges) { Truncated = true; return; }
            if (a is null || b is null) { if (a is not null || b is not null) Add(path, captureRoot ? "unavailable" : a is null ? "added" : "removed", a, b); return; }
            if (a.Value.ValueKind == JsonValueKind.Object && b.Value.ValueKind == JsonValueKind.Object)
            {
                // Enumerating/sorting is bounded independently of the number of resulting differences.
                var left = Properties(a.Value); var right = Properties(b.Value);
                foreach (var key in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
                {
                    Visit(left.TryGetValue(key, out var av) ? av : null, right.TryGetValue(key, out var bv) ? bv : null,
                        path + "/" + Clip(key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)), depth + 1, false);
                    if (Truncated) break;
                }
                return;
            }
            if (a.Value.ValueKind == JsonValueKind.Array && b.Value.ValueKind == JsonValueKind.Array)
            {
                var left = a.Value.EnumerateArray(); var right = b.Value.EnumerateArray();
                int index = 0;
                while (true)
                {
                    bool hasLeft = left.MoveNext(), hasRight = right.MoveNext();
                    if (!hasLeft && !hasRight) break;
                    Visit(hasLeft ? left.Current : null, hasRight ? right.Current : null, path + "/" + index++, depth + 1, false);
                    if (Truncated) break;
                }
                return;
            }
            if (!JsonElement.DeepEquals(a.Value, b.Value)) Add(path,
                a.Value.ValueKind == JsonValueKind.String && b.Value.ValueKind == JsonValueKind.String ? "text" : "value", a, b);
        }
        private Dictionary<string, JsonElement> Properties(JsonElement value)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            int count = 0;
            foreach (var property in value.EnumerateObject())
            {
                if (++count > 2048) { Truncated = true; break; }
                if (!result.TryAdd(property.Name, property.Value)) Truncated = true;
            }
            return result;
        }
        private void Add(string path, string kind, JsonElement? a, JsonElement? b)
        {
            string? left = a?.GetRawText(), right = b?.GetRawText();
            int? offset = null;
            if (kind == "text")
            {
                // Linear first-change window; no edit-distance matrix or quadratic text buffers.
                string av = a!.Value.GetString()!, bv = b!.Value.GetString()!;
                int first = 0;
                while (first < Math.Min(av.Length, bv.Length) && av[first] == bv[first])
                {
                    if (first % 4096 == 0) token.ThrowIfCancellationRequested();
                    first++;
                }
                offset = first;
                left = av[first..]; right = bv[first..];
            }
            bool clipped = path.Length > MaxPreview || left?.Length > MaxPreview || right?.Length > MaxPreview || offset > 0;
            PreviewTruncated |= clipped;
            Changes.Add(new(Clip(path), kind, left is null ? null : Clip(left), right is null ? null : Clip(right), clipped, offset));
        }
        private string Clip(string text)
        {
            if (text.Length <= MaxPreview) return text;
            PreviewTruncated = true;
            return text[..MaxPreview] + "…";
        }
    }
}
