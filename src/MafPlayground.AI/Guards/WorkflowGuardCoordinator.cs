using System.Collections.Concurrent;
using MafPlayground.AI.Guards.Content;

namespace MafPlayground.AI.Guards;

public sealed class WorkflowGuardCoordinator(
    GuardProfileResolver profiles,
    GuardExecutionContextAccessor contextAccessor,
    ContentGuard contentGuard)
{
    private readonly ConcurrentDictionary<string, GuardExecutionContext> _executions =
        new(StringComparer.Ordinal);

    public async ValueTask<GuardedWorkflowInput> StartAsync(
        string profileName,
        string input,
        CancellationToken cancellationToken = default)
    {
        GuardProfileOptions profile = profiles.Resolve(profileName);
        if (input.Length > profile.Content.MaxInputCharacters)
        {
            throw new ContentLengthExceededException(
                input.Length,
                profile.Content.MaxInputCharacters);
        }

        string guardedInput = profile.Content.Enabled
            ? await contentGuard.ApplyAsync(
                input,
                profile.Content.InputAction,
                ContentOrigin.UserInput,
                cancellationToken).ConfigureAwait(false)
            : input;
        string executionId = Guid.NewGuid().ToString("N");
        if (!_executions.TryAdd(
                executionId,
                new GuardExecutionContext(profileName, profile)))
        {
            throw new InvalidOperationException("Could not create a unique workflow guard execution.");
        }

        return new GuardedWorkflowInput(executionId, guardedInput);
    }

    public GuardExecutionScope EnterScope(string executionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        return _executions.TryGetValue(executionId, out GuardExecutionContext? context)
            ? contextAccessor.EnterScope(context)
            : throw new InvalidOperationException(
                $"Workflow guard execution '{executionId}' is unavailable.");
    }

    public async ValueTask<string?> GuardOutputAsync(
        string executionId,
        string? output,
        CancellationToken cancellationToken = default)
    {
        if (output is null)
        {
            return null;
        }

        GuardExecutionContext context = GetRequired(executionId);
        return context.Profile.Content.Enabled
            ? await contentGuard.ApplyAsync(
                output,
                context.Profile.Content.OutputAction,
                ContentOrigin.AgentOutput,
                cancellationToken).ConfigureAwait(false)
            : output;
    }

    public void Complete(string executionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        _executions.TryRemove(executionId, out _);
    }

    private GuardExecutionContext GetRequired(string executionId) =>
        _executions.TryGetValue(executionId, out GuardExecutionContext? context)
            ? context
            : throw new InvalidOperationException(
                $"Workflow guard execution '{executionId}' is unavailable.");
}

public sealed record GuardedWorkflowInput(string ExecutionId, string Content);
