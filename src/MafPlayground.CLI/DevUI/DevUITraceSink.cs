using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MafPlayground.CLI.DevUI;

internal sealed class DevUITraceSink
{
    private const string TraceEventType = "response.trace.completed";
    private readonly ConcurrentQueue<DevUITraceActivity> _activities = new();
    private string? _entityId;
    private string? _responseId;

    public DevUITraceSink(string fallbackResponseId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackResponseId);
        FallbackResponseId = fallbackResponseId;
    }

    public string FallbackResponseId { get; }

    public void Enqueue(DevUITraceActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activities.Enqueue(activity);
    }

    public void ObserveResponseChunk(ReadOnlySpan<byte> chunk)
    {
        string text = Encoding.UTF8.GetString(chunk);
        _responseId ??= ReadJsonString(text, "\"id\":\"");
        _entityId ??= ReadJsonString(text, "\"model\":\"");
    }

    public bool TryDequeueFrame(out byte[]? frame)
    {
        if (!_activities.TryDequeue(out DevUITraceActivity? activity))
        {
            frame = null;
            return false;
        }

        DevUITraceEventData data = new(
            _responseId ?? FallbackResponseId,
            _entityId,
            activity.TraceId,
            activity.SpanId,
            activity.ParentSpanId,
            activity.OperationName,
            activity.StartTime,
            activity.DurationMilliseconds,
            activity.Status,
            activity.Attributes);
        DevUITraceEvent traceEvent = new(TraceEventType, data);
        string json = JsonSerializer.Serialize(traceEvent);
        frame = Encoding.UTF8.GetBytes($"event: {TraceEventType}\ndata: {json}\n\n");
        return true;
    }

    private static string? ReadJsonString(string text, string marker)
    {
        int valueStart = text.IndexOf(marker, StringComparison.Ordinal);
        if (valueStart < 0)
        {
            return null;
        }

        valueStart += marker.Length;
        int valueEnd = text.IndexOf('"', valueStart);
        return valueEnd > valueStart ? text[valueStart..valueEnd] : null;
    }

    private sealed record DevUITraceEvent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("data")] DevUITraceEventData Data);

    private sealed record DevUITraceEventData(
        [property: JsonPropertyName("response_id")] string ResponseId,
        [property: JsonPropertyName("entity_id")] string? EntityId,
        [property: JsonPropertyName("trace_id")] string TraceId,
        [property: JsonPropertyName("span_id")] string SpanId,
        [property: JsonPropertyName("parent_span_id")] string? ParentSpanId,
        [property: JsonPropertyName("operation_name")] string OperationName,
        [property: JsonPropertyName("start_time")] double StartTime,
        [property: JsonPropertyName("duration_ms")] double DurationMilliseconds,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("attributes")] IReadOnlyDictionary<string, object?> Attributes);
}
