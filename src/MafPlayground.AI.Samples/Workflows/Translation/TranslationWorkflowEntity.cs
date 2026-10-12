using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Configuration;
using MafPlayground.AI.Runtime.Workflows;
using Microsoft.Agents.AI.Workflows;

namespace MafPlayground.AI.Workflows.Translation;

public sealed class TranslationWorkflowEntity(
    ITranslationWorkflowSource source,
    IMafWorkflowRunner runner,
    IAIEntityRuntimeOptionsResolver runtimeOptions) :
    IAIEntity<TranslationWorkflowRequest, TranslationWorkflowResult>
{
    public async ValueTask<TranslationWorkflowResult> ExecuteAsync(
        TranslationWorkflowRequest input,
        AIExecutionContext context,
        CancellationToken cancellationToken)
    {
        TranslationWorkflowRequest validated = source.Validate(input);
        return await runner.RunAsync<TranslationWorkflowInput, TranslationWorkflowResult>(
            source.CreateWorkflowForRuntime(
                runtimeOptions.Resolve("translation-workflow").GuardProfile),
            new TranslationWorkflowInput(validated.Text, validated.TargetLanguages),
            cancellationToken).ConfigureAwait(false);
    }
}

public interface ITranslationWorkflowSource : IMafWorkflowSource
{
    Workflow CreateWorkflowForRuntime(string guardProfile);

    TranslationWorkflowRequest Validate(TranslationWorkflowRequest request);
}

internal sealed class TranslationWorkflowSource(TranslationWorkflowFactory factory)
    : ITranslationWorkflowSource
{
    public Workflow CreateWorkflow() => factory.Create();

    public Workflow CreateWorkflowForRuntime(string guardProfile) =>
        factory.CreateForRuntime(guardProfile);

    public Workflow CreateWorkflowForDevUI(string workflowName) =>
        factory.CreateForDevUI(workflowName);

    public TranslationWorkflowRequest Validate(TranslationWorkflowRequest request) =>
        factory.Validate(request);
}
