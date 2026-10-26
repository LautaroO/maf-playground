using MafPlayground.AI.Guards;
using MafPlayground.AI.Runtime.Registry;

namespace MafPlayground.AI.Runtime.Configuration;

public sealed class AIRuntimeOptions
{
    public const string ConfigurationSectionName = "AI";

    public AIEntityRuntimeOptions Defaults { get; set; } = new();

    public Dictionary<string, AIEntityRuntimeOptions> Entities { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AIEntityRuntimeOptions
{
    public string? Model { get; set; }

    public string? GuardProfile { get; set; }

    public TimeSpan? ModelCallTimeout { get; set; }
}

public sealed record ResolvedAIEntityRuntimeOptions(
    AIModelSelection Model,
    string GuardProfile,
    TimeSpan ModelCallTimeout);

public interface IAIEntityRuntimeOptionsResolver
{
    ResolvedAIEntityRuntimeOptions Resolve(string entityId);

    ResolvedAIEntityRuntimeOptions Resolve(string entityId, AIExecutionProfileOptions profile);
}
