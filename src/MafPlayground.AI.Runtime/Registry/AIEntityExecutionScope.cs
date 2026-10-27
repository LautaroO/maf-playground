using MafPlayground.AI.Runtime.Configuration;

namespace MafPlayground.AI.Runtime.Registry;

/// <summary>Immutable chat overrides. Retrieval/embedding configuration is deliberately separate.</summary>
public sealed record AIExecutionProfileOptions(AIModelSelection? Model = null, string? GuardProfile = null, TimeSpan? ModelCallTimeout = null);

public sealed record AIEntityConstructionContext(string ProfileId, ResolvedAIEntityRuntimeOptions RuntimeOptions, Action<IDisposable> RegisterResource)
{
    public T Own<T>(T resource) where T : IDisposable { RegisterResource(resource); return resource; }
}

/// <summary>One binding per execution DI scope, shared by source inspection and typed invocation.</summary>
public sealed class AIEntityExecutionScope(IAIEntityRuntimeOptionsResolver defaults) : IAsyncDisposable, IDisposable
{
    private readonly Dictionary<string, ResolvedAIEntityRuntimeOptions> settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> resources = [];
    private readonly Dictionary<string, object> instances = new(StringComparer.OrdinalIgnoreCase);
    public string? ProfileId { get; private set; }
    public AIExecutionProfileOptions Options { get; private set; } = new();

    public void Bind(string profileId, AIExecutionProfileOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(options);
        if (ProfileId is not null && (ProfileId != profileId || Options != options))
            throw new InvalidOperationException("An execution scope cannot change profiles.");
        if (options.ModelCallTimeout is { } timeout && timeout <= TimeSpan.Zero ||
            options.GuardProfile is not null && string.IsNullOrWhiteSpace(options.GuardProfile))
            throw new ArgumentException("Invalid execution profile options.", nameof(options));
        ProfileId = profileId;
        Options = options;
    }

    public ResolvedAIEntityRuntimeOptions GetRuntimeOptions(string entityId)
    {
        if (ProfileId is null) throw new InvalidOperationException("Bind an execution profile before resolving settings.");
        if (!settings.TryGetValue(entityId, out ResolvedAIEntityRuntimeOptions? value))
        {
            value = defaults.Resolve(entityId, Options);
            settings.Add(entityId, value);
        }
        return value;
    }

    internal object Resolve(AIEntityRegistration registration, IServiceProvider services)
    {
        if (instances.TryGetValue(registration.Descriptor.Id, out object? instance)) return instance;
        ResolvedAIEntityRuntimeOptions options = GetRuntimeOptions(registration.Descriptor.Id);
        instance = registration.ProfileFactory!(services, new(ProfileId!, options, resource => resources.Add(resource)));
        instances.Add(registration.Descriptor.Id, instance);
        return instance;
    }

    public void Dispose()
    {
        foreach (object instance in instances.Values)
        {
            if (instance is IDisposable disposable) disposable.Dispose();
            else if (instance is IAsyncDisposable) throw new InvalidOperationException("Use DisposeAsync for asynchronous execution resources.");
        }
        instances.Clear();
        for (int index = resources.Count - 1; index >= 0; index--) resources[index].Dispose();
        resources.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (object instance in instances.Values)
        {
            if (instance is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (instance is IDisposable disposable) disposable.Dispose();
        }
        instances.Clear();
        for (int index = resources.Count - 1; index >= 0; index--) resources[index].Dispose();
        resources.Clear();
    }
}
