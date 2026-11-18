using System.Diagnostics;
using MafPlayground.AI.Runtime.Execution;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;

namespace MafPlayground.Studio.Capture;

public sealed class StudioActivityListener : IDisposable
{
    private readonly ActivityListener listener;
    public StudioActivityListener(IStudioEventSink sink, StudioPayloadStore payloads, TimeProvider clock)
    {
        string contextKey = "studio." + Guid.NewGuid().ToString("N");
        listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("MafPlayground", StringComparison.Ordinal) ||
                source.Name.StartsWith("Microsoft.Agents.AI", StringComparison.Ordinal) ||
                source.Name.StartsWith("Experimental.Microsoft", StringComparison.Ordinal) || source.Name.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => StudioCaptureScope.Current?.Sink != sink ? ActivitySamplingResult.None : ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => StudioCaptureScope.Current?.Sink != sink ? ActivitySamplingResult.None : ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (StudioCaptureScope.Current is { } scope && scope.Sink == sink)
                    activity.SetCustomProperty(contextKey, new CaptureIdentity(scope.Context.RunId, AIExecutionObservation.CurrentInvocation));
            },
            ActivityStopped = activity =>
            {
                if (activity.GetCustomProperty(contextKey) is not CaptureIdentity identity) return;
                try
                {
                    var tags = Tags(activity.TagObjects);
                    AIInvocationIdentity? invocation = activity.GetCustomProperty(AIExecutionObservation.InvocationActivityProperty) as AIInvocationIdentity ?? identity.Invocation;
                    string? executor = invocation?.ExecutorId;
                    // Pinned native tags identify an executor, never an inferred traversal.
                    executor ??= activity.GetTagItem("executor.id") as string;
                    if (executor is not null) executor = payloads.Redactor.RedactText(executor);
                    StudioSpan span = new(activity.TraceId.ToString(), activity.SpanId.ToString(), activity.ParentSpanId.ToString(),
                        payloads.Redactor.RedactText(activity.Source.Name), payloads.Redactor.RedactText(activity.DisplayName), activity.StartTimeUtc,
                        activity.Duration, activity.Status.ToString(), executor, invocation?.InvocationId,
                        invocation is null ? "executor_identity_unavailable" : null,
                        Number<long>(activity.GetTagItem("gen_ai.usage.input_tokens")), Number<long>(activity.GetTagItem("gen_ai.usage.output_tokens")),
                        Number<double>(activity.GetTagItem("maf_playground.gen_ai.cost")), activity.GetTagItem("maf_playground.gen_ai.cost.currency") as string,
                        payloads.Capture(tags, "application/json"), payloads.Capture(activity.Events.Select(item => new { item.Name, item.Timestamp, Tags = Tags(item.Tags) }), "application/json"),
                        payloads.Capture(activity.Links.Select(link => new { traceId = link.Context.TraceId.ToString(), spanId = link.Context.SpanId.ToString() }), "application/json"));
                    StudioPayload captured = payloads.Capture(span, "application/json");
                    StudioCaptureState state = new[] { captured.State, span.Attributes.State, span.Events.State, span.Links.State }.Max();
                    sink.Record(new(identity.RunId, 0, clock.GetUtcNow(), "span", executor, invocation?.InvocationId, span.SpanId, captured.Value, state));
                }
                catch (Exception) { }
            },
        };
        ActivitySource.AddActivityListener(listener);
    }
    public void Dispose() => listener.Dispose();
    private sealed record CaptureIdentity(string RunId, AIInvocationIdentity? Invocation);
    private static Dictionary<string, object?> Tags(IEnumerable<KeyValuePair<string, object?>> tags)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach (var tag in tags) result[tag.Key] = tag.Value;
        return result;
    }
    private static T? Number<T>(object? value) where T : struct, IConvertible
    {
        if (value is null) return null;
        try { return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception) { return null; }
    }
}
