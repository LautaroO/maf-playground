using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Sessions;
using MafPlayground.Studio.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Execution;

/// <summary>Bounded host-lifetime session handles. Admission and reset share a gate, including queued turns.</summary>
public sealed class StudioSessionService(IStudioRunService runs, IServiceScopeFactory scopes, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<string, StudioSession> sessions = new(StringComparer.Ordinal);
    private const int Capacity = 1000;

    public async ValueTask<IReadOnlyList<StudioSession>> ListAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return sessions.Values.ToArray(); }
        finally { gate.Release(); }
    }

    public async ValueTask<StudioSession> CreateAsync(StudioSessionRequest request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (sessions.Count >= Capacity) throw new StudioCapacityException("Session capacity is full.");
            StudioSession session = new(Guid.NewGuid().ToString("N"), request.EntityId, request.ProfileId, clock.GetUtcNow());
            sessions.Add(session.Id, session);
            return session;
        }
        finally { gate.Release(); }
    }

    public async ValueTask<StudioRunReceipt> StartAsync(StudioRunRequest request, AICallerContext caller, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (request.SessionId is { } id)
            {
                StudioSession session = Required(id);
                if (session.EntityId != request.EntityId || session.ProfileId != request.ProfileId)
                    throw new StudioSessionConflictException();
            }
            return await runs.StartAsync(request, caller, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async ValueTask ResetAsync(string id, AICallerContext caller, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StudioSession session = Required(id);
            if ((await runs.ListAsync(cancellationToken).ConfigureAwait(false)).Any(run => !run.IsTerminal && run.Request.SessionId == id))
                throw new StudioSessionBusyException();
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAgentSessionStore>();
            await store.ResetAsync(new(session.EntityId, caller.TenantId, caller.SubjectId, id, session.ProfileId), cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private StudioSession Required(string id) => sessions.GetValueOrDefault(id) ?? throw new KeyNotFoundException();
}
public sealed class StudioSessionConflictException() : InvalidOperationException("Session entity and profile are immutable.");
