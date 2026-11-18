using MafPlayground.AI.Configuration;
using MafPlayground.Studio.Contracts;
using MafPlayground.AI.Runtime.Registry;

namespace MafPlayground.Studio.Execution;

/// <summary>Immutable, host-configured chat configuration; no provider credentials or embedding settings.</summary>
public sealed record StudioExecutionProfile(string Id, AIModelSelection? Model = null, string? GuardProfile = null, TimeSpan? ModelCallTimeout = null);

public sealed class StudioExecutionProfileBinder(AIEntityExecutionScope scope, IEnumerable<StudioExecutionProfile> profiles, StudioOptions options, IAIEntityInspection inspection)
{
    private readonly IReadOnlyDictionary<string, StudioExecutionProfile> profiles = profiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);

    internal bool HasOverrides => scope.Options != new AIExecutionProfileOptions();

    public StudioExecutionProfileSnapshot BindForEntity(string entityId, string profileId)
    {
        Bind(profileId);
        if (!inspection.GetRegistration(entityId).SupportsProfiles) return new(profileId, null, null, null);
        var runtime = scope.GetRuntimeOptions(entityId);
        return new(profileId, runtime.Model.ToString(), runtime.GuardProfile, runtime.ModelCallTimeout);
    }

    public void Bind(string profileId)
    {
        if (!profiles.TryGetValue(profileId, out StudioExecutionProfile? profile))
        {
            if (profileId != options.DefaultProfileId) throw new InvalidOperationException("The execution profile is not configured.");
            profile = new(profileId);
        }
        if (profile.ModelCallTimeout is { } timeout && timeout <= TimeSpan.Zero) throw new InvalidOperationException("The profile timeout must be positive.");
        if (profile.GuardProfile is not null && string.IsNullOrWhiteSpace(profile.GuardProfile)) throw new InvalidOperationException("The profile guard name is invalid.");
        scope.Bind(profileId, new(profile.Model, profile.GuardProfile, profile.ModelCallTimeout));
    }
}
