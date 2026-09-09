using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Tests.AI.Foundation;

public sealed class InMemoryAgentSessionStoreTests
{
    [Fact]
    public async Task Store_EnforcesOwnershipAndOptimisticConcurrency()
    {
        ServiceCollection services = new();
        services.AddAICore(AIModelSelection.Parse("fake:model"));
        services.AddInMemoryAgentSessions();
        using ServiceProvider provider = services.BuildServiceProvider();
        IAgentSessionStore store = provider.GetRequiredService<IAgentSessionStore>();
        AgentSessionKey owner = new("agent", "tenant", "subject", "session");
        using JsonDocument firstState = JsonDocument.Parse("{\"turn\":1}");
        StoredAgentSession created = await store.SaveAsync(
            owner,
            firstState.RootElement,
            null,
            CancellationToken.None);

        StoredAgentSession loaded = Assert.IsType<StoredAgentSession>(
            await store.LoadAsync(owner, CancellationToken.None));
        Assert.Equal(0, loaded.Version);
        Assert.Equal(1, loaded.State.GetProperty("turn").GetInt32());

        AgentSessionKey intruder = owner with { SubjectId = "other" };
        await Assert.ThrowsAsync<AISessionOwnershipException>(async () =>
            await store.LoadAsync(intruder, CancellationToken.None));

        using JsonDocument secondState = JsonDocument.Parse("{\"turn\":2}");
        _ = await store.SaveAsync(
            owner,
            secondState.RootElement,
            created.Version,
            CancellationToken.None);
        await Assert.ThrowsAsync<AISessionConcurrencyException>(async () =>
            await store.SaveAsync(
                owner,
                secondState.RootElement,
                created.Version,
                CancellationToken.None));
    }

    [Fact]
    public async Task Store_ExpiresEntriesUsingInjectedTimeProvider()
    {
        ManualTimeProvider time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(time);
        services.AddAICore(AIModelSelection.Parse("fake:model"));
        services.AddInMemoryAgentSessions(options =>
            options.Retention = TimeSpan.FromMinutes(1));
        using ServiceProvider provider = services.BuildServiceProvider();
        IAgentSessionStore store = provider.GetRequiredService<IAgentSessionStore>();
        AgentSessionKey key = new("agent", null, "subject", "session");
        using JsonDocument state = JsonDocument.Parse("{}");
        _ = await store.SaveAsync(key, state.RootElement, null, CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(await store.LoadAsync(key, CancellationToken.None));
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
