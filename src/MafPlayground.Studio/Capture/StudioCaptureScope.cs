using MafPlayground.AI.Runtime.Execution;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using System.Text.Json;

namespace MafPlayground.Studio.Capture;

public sealed class StudioCaptureScope : IDisposable
{
    private static readonly AsyncLocal<StudioCaptureScope?> Ambient = new();
    private readonly StudioCaptureScope? previous;
    private readonly IDisposable observation;
    private readonly IStudioEventSink sink;
    private readonly StudioPayloadStore payloads;
    private readonly TimeProvider clock;
    internal IStudioEventSink Sink => sink;
    public StudioExecutionContext Context { get; }
    public static StudioCaptureScope? Current => Ambient.Value;

    public StudioCaptureScope(StudioExecutionContext context, IStudioEventSink sink, StudioPayloadStore payloads, TimeProvider clock)
    {
        Context = context;
        this.sink = sink;
        this.payloads = payloads;
        this.clock = clock;
        previous = Ambient.Value;
        Ambient.Value = this;
        observation = AIExecutionObservation.Observe(item => Record(item.Kind, item.Value, item.ExecutorId, item.InvocationId, item.SpanId));
    }
    public void Record(string kind, object? value, string? executorId = null, string? invocationId = null, string? spanId = null)
    {
        try
        {
            StudioPayload payload = payloads.Capture(value, "application/json");
            sink.Record(new(Context.RunId, 0, clock.GetUtcNow(), payloads.Redactor.RedactText(kind),
                executorId is null ? null : payloads.Redactor.RedactText(executorId), invocationId, spanId, JsonSerializer.SerializeToElement(payload), payload.State));
        }
        catch (Exception) { /* Capture is observational, including user-provided sinks. */ }
    }
    public void Dispose() { observation.Dispose(); Ambient.Value = previous; }
}
