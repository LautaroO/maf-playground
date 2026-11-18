using MafPlayground.AI.Configuration;

namespace MafPlayground.Studio.Execution;

/// <summary>Only host-configured profile identities cross the transport boundary.</summary>
public sealed class StudioProfileCatalog
{
    public IReadOnlyList<string> Ids { get; }
    public StudioProfileCatalog(IEnumerable<StudioExecutionProfile> profiles, StudioOptions options)
    {
        StudioExecutionProfile[] configured = profiles.ToArray();
        if (configured.GroupBy(profile => profile.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Duplicate Studio profiles.");
        foreach (var profile in configured)
        {
            _ = ParseModel(profile.Model?.ToString());
            if (profile.GuardProfile is not null && !IsSafeId(profile.GuardProfile) || profile.ModelCallTimeout is { } timeout && timeout <= TimeSpan.Zero)
                throw new ArgumentException("Invalid configured Studio profile settings.");
        }
        string[] ids = configured.Select(profile => profile.Id).Prepend(options.DefaultProfileId).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Any(id => !IsSafeId(id))) throw new ArgumentException("Invalid configured Studio profile identity.");
        Ids = Array.AsReadOnly(ids);
    }
    public void Validate(string id)
    {
        if (!Ids.Contains(id, StringComparer.Ordinal)) throw new ArgumentException("The execution profile is not configured.");
    }
    public static AIModelSelection? ParseModel(string? value)
    {
        if (value is null) return null;
        if (value.Length > 200 || value.Contains("://", StringComparison.Ordinal) ||
            !value.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '-' or '_' or '.' or '/') ||
            !AIModelSelection.TryParse(value, out var model)) throw new ArgumentException("Invalid Studio model selector.");
        return model;
    }
    internal static bool IsSafeId(string? id) => id is { Length: > 0 and <= 100 } && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
