using System.Text.Json;
using MafPlayground.AI.Abstractions;
using Microsoft.Agents.AI.Workflows;

namespace MafPlayground.AI.Runtime.Workflows;

public interface IMafWorkflowRunner
{
    ValueTask<TOutput> RunAsync<TInput, TOutput>(
        Workflow workflow,
        TInput input,
        CancellationToken cancellationToken,
        IAIExecutionObserver? observer = null)
        where TInput : notnull;
}

public interface IMafWorkflowSource
{
    Workflow CreateWorkflow();

    Workflow CreateWorkflowForDevUI(string workflowName);
}

internal sealed class MafWorkflowRunner : IMafWorkflowRunner
{
    public async ValueTask<TOutput> RunAsync<TInput, TOutput>(
        Workflow workflow,
        TInput input,
        CancellationToken cancellationToken,
        IAIExecutionObserver? observer = null)
        where TInput : notnull
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (observer is not null)
        {
            await using StreamingRun stream = await InProcessExecution.RunStreamingAsync(workflow, input,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            TOutput? result = default;
            bool hasOutput = false;
            await foreach (WorkflowEvent item in stream.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                string? executorId = item switch
                {
                    ExecutorInvokedEvent invoked => invoked.ExecutorId,
                    ExecutorCompletedEvent completed => completed.ExecutorId,
                    ExecutorFailedEvent failed => failed.ExecutorId,
                    WorkflowOutputEvent output => output.ExecutorId,
                    _ => null,
                };
                observer.Record(new("workflow_event", JsonSerializer.SerializeToElement(new { eventType = item.GetType().Name }), executorId));
                if (item is WorkflowOutputEvent streamedOutput && streamedOutput.Is<TOutput>())
                {
                    result = streamedOutput.As<TOutput>();
                    hasOutput = true;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return hasOutput ? result! : throw new InvalidOperationException("The workflow completed without producing the expected output.");
        }
        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            input,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        WorkflowOutputEvent? outputEvent = run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .LastOrDefault(output => output.Is<TOutput>());
        if (outputEvent is null)
        {
            throw new InvalidOperationException(
                "The workflow completed without producing the expected output.");
        }

        return outputEvent.As<TOutput>()!;
    }
}
