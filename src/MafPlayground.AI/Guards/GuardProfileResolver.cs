using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Guards;

public sealed class GuardProfileResolver(IOptions<AIGuardOptions> options)
{
    private readonly AIGuardOptions _options = options.Value;

    public GuardProfileOptions Resolve(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        return _options.Profiles.TryGetValue(profileName, out GuardProfileOptions? profile)
            ? profile
            : throw new GuardConfigurationException(
                $"AI guard profile '{profileName}' is not configured. Available profiles: " +
                $"{string.Join(", ", _options.Profiles.Keys.Order(StringComparer.OrdinalIgnoreCase))}.");
    }
}

public sealed class GuardConfigurationException(string message)
    : InvalidOperationException(message);

