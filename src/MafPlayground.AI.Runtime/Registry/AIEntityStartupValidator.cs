using MafPlayground.AI.Runtime.Configuration;
using MafPlayground.AI.Runtime.Sessions;
using MafPlayground.AI.Guards;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.AI.Runtime.Registry;

internal sealed class AIEntityStartupValidator(
    IEnumerable<AIEntityRegistration> registrations,
    IAIEntityRuntimeOptionsResolver runtimeOptions,
    AIProviderRegistry providers,
    GuardProfileResolver guardProfiles,
    IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<AIStartupValidationOptions>()?.Mode == AIStartupValidationMode.LocalInspection)
            return Task.CompletedTask;
        foreach (AIEntityRegistration registration in registrations)
        {
            ResolvedAIEntityRuntimeOptions options = runtimeOptions.Resolve(
                registration.Descriptor.Id);
            if (!providers.ProviderNames.Contains(
                    options.Model.Provider,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new AIProviderNotFoundException(
                    options.Model.Provider,
                    providers.ProviderNames);
            }

            _ = guardProfiles.Resolve(options.GuardProfile);
            if (registration.Descriptor.SupportsSessions &&
                services.GetService<IAgentSessionStore>() is null)
            {
                throw new InvalidOperationException(
                    $"AI entity '{registration.Descriptor.Id}' enables sessions but no " +
                    "IAgentSessionStore is registered.");
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
