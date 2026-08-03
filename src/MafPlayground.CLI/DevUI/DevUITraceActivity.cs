using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;

namespace MafPlayground.CLI.DevUI;

internal sealed record DevUITraceActivity(
    [property: JsonPropertyName("trace_id")] string TraceId,
    [property: JsonPropertyName("span_id")] string SpanId,
    [property: JsonPropertyName("parent_span_id")] string? ParentSpanId,
    [property: JsonPropertyName("operation_name")] string OperationName,
    [property: JsonPropertyName("start_time")] double StartTime,
    [property: JsonPropertyName("duration_ms")] double DurationMilliseconds,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("attributes")] IReadOnlyDictionary<string, object?> Attributes)
{
    public static DevUITraceActivity FromActivity(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        Dictionary<string, object?> attributes = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
        {
            attributes[tag.Key] = NormalizeAttributeValue(tag.Value);
        }
        double startTime = new DateTimeOffset(activity.StartTimeUtc)
            .ToUnixTimeMilliseconds() / 1000d;

        return new DevUITraceActivity(
            activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(),
            activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
            activity.DisplayName,
            startTime,
            activity.Duration.TotalMilliseconds,
            activity.Status switch
            {
                ActivityStatusCode.Ok => "OK",
                ActivityStatusCode.Error => "ERROR",
                _ => "StatusCode.UNSET",
            },
            attributes);
    }

    private static object? NormalizeAttributeValue(object? value) => value switch
    {
        null => null,
        string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or
            float or double or decimal => value,
        IEnumerable<string> strings => strings.ToArray(),
        IEnumerable<bool> booleans => booleans.ToArray(),
        IEnumerable<int> integers => integers.ToArray(),
        IEnumerable<long> longs => longs.ToArray(),
        IEnumerable<double> doubles => doubles.ToArray(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
