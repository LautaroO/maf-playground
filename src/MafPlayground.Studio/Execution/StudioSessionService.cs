using System.Security.Cryptography;
using System.Text;
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
    private readonly string sessionNamespace = Guid.NewGuid().ToString("N");

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

    public async ValueTask<StudioRunReceipt> StartNewSessionAsync(StudioRunRequest request, AICallerContext caller, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (request.SessionId is not null) throw new ArgumentException();
            // A stable host-local handle lets the run store's existing request tombstone
            // validate retries even after the snapshot is evicted. No second retry registry.
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionNamespace + request.RequestId)));
            bool created = !sessions.ContainsKey(id);
            if (created)
            {
                if (sessions.Count >= Capacity) throw new StudioCapacityException("Session capacity is full.");
                sessions.Add(id, new(id, request.EntityId, request.ProfileId, clock.GetUtcNow()));
            }
            try { return await runs.StartAsync(request with { SessionId = id }, caller, cancellationToken).ConfigureAwait(false); }
            catch { if (created) sessions.Remove(id); throw; }
        }
        finally { gate.Release(); }
    }

    public async ValueTask<StudioSession> ResetAndCreateAsync(string id, AICallerContext caller, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StudioSession session = Required(id);
            if (sessions.Count >= Capacity) throw new StudioCapacityException("Session capacity is full.");
            await ResetCoreAsync(session, caller, cancellationToken).ConfigureAwait(false);
            var replacement = session with { Id = Guid.NewGuid().ToString("N"), CreatedAt = clock.GetUtcNow() };
            sessions.Add(replacement.Id, replacement);
            return replacement;
        }
        finally { gate.Release(); }
    }

    private async ValueTask ResetCoreAsync(StudioSession session, AICallerContext caller, CancellationToken cancellationToken)
    {
        if ((await runs.ListAsync(cancellationToken).ConfigureAwait(false)).Any(run => !run.IsTerminal && run.Request.SessionId == session.Id))
            throw new StudioSessionBusyException();
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentSessionStore>();
        await store.ResetAsync(new(session.EntityId, caller.TenantId, caller.SubjectId, session.Id, session.ProfileId), cancellationToken).ConfigureAwait(false);
    }

    private StudioSession Required(string id) => sessions.GetValueOrDefault(id) ?? throw new KeyNotFoundException();
}
public sealed class StudioSessionConflictException() : InvalidOperationException("Session entity and profile are immutable.");
