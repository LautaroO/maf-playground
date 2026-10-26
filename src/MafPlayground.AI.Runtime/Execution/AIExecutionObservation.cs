using System.Diagnostics;
using MafPlayground.AI.Observability;

namespace MafPlayground.AI.Runtime.Execution;

/// <summary>Optional in-process observation. Payloads never enter activities or metric labels.</summary>
public sealed record AIObservedPayload(string Kind, object? Value, string? ExecutorId = null, string? InvocationId = null, string? SpanId = null);
public sealed record AIInvocationIdentity(string ExecutorId, string InvocationId);

public static class AIExecutionObservation
{
    public const string InvocationActivityProperty = "MafPlayground.Execution.Invocation";
    private static readonly AsyncLocal<Action<AIObservedPayload>?> Observer = new();
    private static readonly AsyncLocal<AIInvocationIdentity?> Invocation = new();
    public static AIInvocationIdentity? CurrentInvocation => Invocation.Value;
    public static IDisposable Observe(Action<AIObservedPayload> observer)
    {
        var previous = Observer.Value;
        Observer.Value = observer;
        return new Restore(() => Observer.Value = previous);
    }
    public static AIInvocationObservation BeginInvocation(string executorId, object? input) => new(executorId, input);
    public static void Record(string kind, object? value, string? executorId = null)
    {
        try { Observer.Value?.Invoke(new(kind, value, executorId ?? Invocation.Value?.ExecutorId, Invocation.Value?.InvocationId, Activity.Current?.SpanId.ToString())); }
        catch (Exception) { /* Inspection must never change application execution. */ }
    }
    public sealed class AIInvocationObservation : IDisposable
    {
        private readonly AIInvocationIdentity? previous;
        internal AIInvocationObservation(string executorId, object? input)
        {
            previous = Invocation.Value;
            if (Observer.Value is null) return;
            Invocation.Value = new(executorId, Guid.NewGuid().ToString("N"));
            // Only attach to a native executor activity whose explicit ID agrees.
            if (Activity.Current is { } activity && activity.Source.Name == AITelemetry.WorkflowSourceName &&
                activity.GetTagItem("executor.id") is string nativeId && nativeId == executorId)
                activity.SetCustomProperty(InvocationActivityProperty, Invocation.Value);
            Record("executor_started", new { observation = "invocation_boundary" });
            Record("executor_input", input);
        }
        private bool terminated;
        public void Complete()
        {
            if (terminated) return;
            terminated = true;
            Record("executor_completed", new { observation = "invocation_return" });
        }
        public void Failed(Exception exception)
        {
            if (terminated) return;
            terminated = true;
            Record(exception is OperationCanceledException ? "executor_cancelled" : "executor_failed",
                new { observation = "invocation_exception", type = exception.GetType().Name, exception.Message });
        }
        public void Output(object? output) => Record("executor_output", output);
        public void Send(object? message, string? targetId = null) => Record("message_sent", new { targetId, message });
        public void Dispose() => Invocation.Value = previous;
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}
