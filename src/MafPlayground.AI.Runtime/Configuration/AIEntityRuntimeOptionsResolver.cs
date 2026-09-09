using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Runtime.Configuration;

internal sealed class AIEntityRuntimeOptionsResolver(
    IOptions<AIRuntimeOptions> options,
    IOptions<MafPlayground.AI.Resilience.AIResilienceOptions> legacyResilience)
    : IAIEntityRuntimeOptionsResolver
{
    public ResolvedAIEntityRuntimeOptions Resolve(string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        AIRuntimeOptions configuration = options.Value;
        _ = configuration.Entities.TryGetValue(entityId, out AIEntityRuntimeOptions? entity);
        string? model = entity?.Model ?? configuration.Defaults.Model;
        if (!AIModelSelection.TryParse(model, out AIModelSelection? selection))
        {
            throw new InvalidOperationException(
                $"AI entity '{entityId}' requires a model in provider:model format.");
        }

        string guardProfile = entity?.GuardProfile ?? configuration.Defaults.GuardProfile ??
            MafPlayground.AI.Guards.GuardProfileNames.Default;
        TimeSpan timeout = entity?.ModelCallTimeout ?? configuration.Defaults.ModelCallTimeout ??
            legacyResilience.Value.ModelCallTimeout;
        if (string.IsNullOrWhiteSpace(guardProfile) || timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"AI entity '{entityId}' has invalid guard or timeout configuration.");
        }

        return new ResolvedAIEntityRuntimeOptions(selection, guardProfile, timeout);
    }
}
