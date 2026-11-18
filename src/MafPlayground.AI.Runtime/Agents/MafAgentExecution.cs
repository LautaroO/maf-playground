using System.Runtime.CompilerServices;
using System.Text.Json;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Sessions;
using Microsoft.Agents.AI;

namespace MafPlayground.AI.Runtime.Agents;

/// <summary>Shared typed-entity execution with transactional session persistence and optional streaming.</summary>
public static class MafAgentExecution
{
    public static async ValueTask<string> RunAsync(AIAgent agent, string entityId, string message,
        AIExecutionContext context, IAgentSessionStore? sessions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new AIEntityInputException("A non-empty message is required.");
        AgentSessionKey? key = context.SessionId is null ? null : new(entityId, context.Caller.TenantId,
            context.Caller.SubjectId, context.SessionId, context.ProfileId);
        if (key is not null && sessions is null) throw new InvalidOperationException("Session support requires an explicit IAgentSessionStore registration.");
        using IDisposable? lease = key is null ? null : sessions!.Acquire(key);
        StoredAgentSession? stored = key is null ? null : await sessions!.LoadAsync(key, cancellationToken).ConfigureAwait(false);
        AgentSession? session = key is null ? null : stored is null
            ? await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false)
            : await agent.DeserializeSessionAsync(stored.State, cancellationToken: cancellationToken).ConfigureAwait(false);
        AgentResponse response = context.Observer is null
            ? await agent.RunAsync(message, session, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await Observe(agent.RunStreamingAsync(message, session, cancellationToken: cancellationToken), context.Observer, cancellationToken)
                .ToAgentResponseAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (key is not null)
        {
            JsonElement state = await agent.SerializeSessionAsync(session!, cancellationToken: cancellationToken).ConfigureAwait(false);
            await sessions!.SaveAsync(key, state, stored?.Version, cancellationToken).ConfigureAwait(false);
        }
        return response.Text;
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> Observe(IAsyncEnumerable<AgentResponseUpdate> updates,
        IAIExecutionObserver observer, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (AgentResponseUpdate update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text)) observer.Record(new("text_delta", JsonSerializer.SerializeToElement(new { text = update.Text })));
            yield return update;
        }
    }
}
