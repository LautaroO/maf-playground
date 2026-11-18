using System.Collections.Concurrent;
using System.Text.Json;
using MafPlayground.AI.Abstractions;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Runtime.Sessions;

public sealed record AgentSessionKey(
    string EntityId,
    string? TenantId,
    string SubjectId,
    string SessionId,
    string? ProfileId = null);

public sealed record StoredAgentSession(
    AgentSessionKey Key,
    JsonElement State,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt);

public interface IAgentSessionStore
{
    IDisposable Acquire(AgentSessionKey key);

    ValueTask ResetAsync(AgentSessionKey key, CancellationToken cancellationToken);

    ValueTask<StoredAgentSession?> LoadAsync(
        AgentSessionKey key,
        CancellationToken cancellationToken);

    ValueTask<StoredAgentSession> SaveAsync(
        AgentSessionKey key,
        JsonElement state,
        long? expectedVersion,
        CancellationToken cancellationToken);
}

public sealed class InMemoryAgentSessionOptions
{
    public int Capacity { get; set; } = 1_000;

    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(8);
}

internal sealed class InMemoryAgentSessionStore(
    IOptions<InMemoryAgentSessionOptions> options,
    TimeProvider timeProvider) : IAgentSessionStore
{
    private readonly ConcurrentDictionary<string, StoredAgentSession> _sessions =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AgentSessionKey> _owners =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly HashSet<string> _active = new(StringComparer.Ordinal);

    public IDisposable Acquire(AgentSessionKey key)
    {
        ValidateKey(key);
        lock (_gate)
        {
            RemoveExpired();
            string identity = OwnerIdentity(key.EntityId, key.SessionId);
            if (_owners.TryGetValue(identity, out AgentSessionKey? owner)) EnsureOwner(owner, key);
            if (!_active.Add(identity)) throw new AISessionConcurrencyException(key.SessionId);
            return new Lease(() => { lock (_gate) _active.Remove(identity); });
        }
    }

    public ValueTask ResetAsync(AgentSessionKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        lock (_gate)
        {
            string identity = OwnerIdentity(key.EntityId, key.SessionId);
            if (_active.Contains(identity)) throw new AISessionConcurrencyException(key.SessionId);
            if (_owners.TryGetValue(identity, out AgentSessionKey? owner)) EnsureOwner(owner, key);
            _sessions.TryRemove(CompositeIdentity(key), out _);
            _owners.TryRemove(identity, out _);
        }
        return ValueTask.CompletedTask;
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? release = release;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }


    public ValueTask<StoredAgentSession?> LoadAsync(
        AgentSessionKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        lock (_gate)
        {
            RemoveExpired();
            string ownerIdentity = OwnerIdentity(key.EntityId, key.SessionId);
            if (!_owners.TryGetValue(ownerIdentity, out AgentSessionKey? owner))
            {
                return ValueTask.FromResult<StoredAgentSession?>(null);
            }

            EnsureOwner(owner, key);
            return ValueTask.FromResult(
                _sessions.TryGetValue(CompositeIdentity(key), out StoredAgentSession? session)
                    ? session
                    : null);
        }
    }

    public ValueTask<StoredAgentSession> SaveAsync(
        AgentSessionKey key,
        JsonElement state,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        if (state.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Agent session state is required.", nameof(state));
        }
        lock (_gate)
        {
            RemoveExpired();
            string ownerIdentity = OwnerIdentity(key.EntityId, key.SessionId);
            if (_owners.TryGetValue(ownerIdentity, out AgentSessionKey? owner))
            {
                EnsureOwner(owner, key);
            }
            string identity = CompositeIdentity(key);
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (_sessions.TryGetValue(identity, out StoredAgentSession? current))
            {
                if (expectedVersion != current.Version)
                {
                    throw new AISessionConcurrencyException(key.SessionId);
                }

                StoredAgentSession updated = current with
                {
                    State = state.Clone(),
                    Version = current.Version + 1,
                    UpdatedAt = now,
                    ExpiresAt = now + options.Value.Retention,
                };
                _sessions[identity] = updated;
                return ValueTask.FromResult(updated);
            }

            if (expectedVersion is not null)
            {
                throw new AISessionConcurrencyException(key.SessionId);
            }

            if (_sessions.Count >= options.Value.Capacity)
            {
                StoredAgentSession oldest = _sessions.Values
                    .Where(item => !_active.Contains(OwnerIdentity(item.Key.EntityId, item.Key.SessionId)))
                    .MinBy(item => item.UpdatedAt) ?? throw new InvalidOperationException("All session capacity is active.");
                _sessions.TryRemove(CompositeIdentity(oldest.Key), out _);
                _owners.TryRemove(
                    OwnerIdentity(oldest.Key.EntityId, oldest.Key.SessionId),
                    out _);
            }

            StoredAgentSession created = new(
                key,
                state.Clone(),
                0,
                now,
                now,
                now + options.Value.Retention);
            _sessions[identity] = created;
            _owners[ownerIdentity] = key;
            return ValueTask.FromResult(created);
        }
    }

    private void RemoveExpired()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach ((string key, StoredAgentSession value) in _sessions)
        {
            if (value.ExpiresAt <= now && !_active.Contains(OwnerIdentity(value.Key.EntityId, value.Key.SessionId)))
            {
                _sessions.TryRemove(key, out _);
                _owners.TryRemove(
                    OwnerIdentity(value.Key.EntityId, value.Key.SessionId),
                    out _);
            }
        }
    }

    private static void EnsureOwner(AgentSessionKey actual, AgentSessionKey requested)
    {
        if (!string.Equals(actual.SubjectId, requested.SubjectId, StringComparison.Ordinal) ||
            !string.Equals(actual.TenantId, requested.TenantId, StringComparison.Ordinal))
        {
            throw new AISessionOwnershipException(requested.SessionId);
        }
        if (!string.Equals(actual.ProfileId, requested.ProfileId, StringComparison.Ordinal))
            throw new InvalidOperationException("A session cannot continue with a different execution profile.");
    }

    private static void ValidateKey(AgentSessionKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.EntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.SubjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.SessionId);
    }

    private static string OwnerIdentity(string entityId, string sessionId) =>
        $"{entityId.ToUpperInvariant()}\u001f{sessionId}";

    private static string CompositeIdentity(AgentSessionKey key) =>
        $"{key.EntityId.ToUpperInvariant()}\u001f{key.TenantId}\u001f{key.SubjectId}\u001f{key.SessionId}";
}
