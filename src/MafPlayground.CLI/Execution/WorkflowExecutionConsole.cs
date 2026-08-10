using System.Diagnostics;
using Microsoft.Agents.AI.Workflows;

namespace MafPlayground.CLI.Execution;

internal sealed class WorkflowExecutionConsole(TextWriter output)
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();

    public async Task RenderAsync(
        WorkflowEvent workflowEvent,
        CancellationToken cancellationToken = default)
    {
        string? message = workflowEvent switch
        {
            WorkflowStartedEvent => "workflow started",
            ExecutorInvokedEvent invoked => $"▶ {invoked.ExecutorId}",
            ExecutorCompletedEvent completed => $"✓ {completed.ExecutorId}",
            ExecutorFailedEvent failed =>
                $"✗ {failed.ExecutorId}: {failed.Data?.Message ?? "unknown failure"}",
            SuperStepStartedEvent started => $"super-step {started.StepNumber} started",
            SuperStepCompletedEvent completed => $"super-step {completed.StepNumber} completed",
            WorkflowOutputEvent outputEvent => $"output from {outputEvent.ExecutorId}",
            WorkflowErrorEvent error => $"workflow failed: {error.Data}",
            WorkflowWarningEvent warning => $"workflow warning: {warning.Data}",
            _ => null,
        };

        if (message is null)
        {
            return;
        }

        await output.WriteLineAsync($"[{_elapsed.Elapsed:hh\\:mm\\:ss\\.fff}] {message}");
        await output.FlushAsync(cancellationToken);
    }
}
