using Microsoft.Agents.AI.Workflows;

namespace MafPlayground.AI.Runtime.Workflows;

public interface IMafWorkflowRunner
{
    ValueTask<TOutput> RunAsync<TInput, TOutput>(
        Workflow workflow,
        TInput input,
        CancellationToken cancellationToken)
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
        CancellationToken cancellationToken)
        where TInput : notnull
    {
        ArgumentNullException.ThrowIfNull(workflow);
        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            input,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        TOutput? result = run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.As<TOutput>())
            .LastOrDefault(output => output is not null);
        return result ?? throw new InvalidOperationException(
            "The workflow completed without producing the expected output.");
    }
}
