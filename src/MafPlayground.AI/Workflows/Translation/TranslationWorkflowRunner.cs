using Microsoft.Agents.AI.Workflows;

namespace MafPlayground.AI.Workflows.Translation;

public sealed class TranslationWorkflowRunner(TranslationWorkflowFactory workflowFactory)
{
    public async Task<TranslationWorkflowResult> RunAsync(
        TranslationWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        TranslationWorkflowRequest validatedRequest = workflowFactory.Validate(request);
        Workflow workflow = workflowFactory.Create(validatedRequest.TargetLanguages);
        await using Run run = await InProcessExecution.RunAsync(
            workflow,
            new TranslationWorkflowInput(validatedRequest.Text),
            cancellationToken: cancellationToken);

        TranslationWorkflowResult? result = run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .Select(output => output.As<TranslationWorkflowResult>())
            .LastOrDefault(output => output is not null);
        return result ?? throw new InvalidOperationException(
            "The translation workflow completed without producing a result.");
    }
}
