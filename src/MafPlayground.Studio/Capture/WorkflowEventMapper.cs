using MafPlayground.AI.Abstractions;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;

namespace MafPlayground.Studio.Capture;

internal sealed class WorkflowEventMapper(IStudioEventSink sink, StudioPayloadStore payloads, TimeProvider clock, string runId) : IAIExecutionObserver
{
    public void Record(AIExecutionEvent item)
    {
        try
        {
            var payload = payloads.Capture(item.Data, "application/json");
            sink.Record(new(runId, 0, clock.GetUtcNow(), item.Kind, payloads.Redactor.RedactText(item.ExecutorId ?? "") is { Length: > 0 } id ? id : null,
                item.InvocationId, null, payload.Value, payload.State));
        }
        catch (Exception) { }
    }
}
