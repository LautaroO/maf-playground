using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MafPlayground.AI.Agents.BasicAgent;

public static class BasicAgentServiceCollectionExtensions
{
    public static IServiceCollection AddBasicAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<BasicAgentOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.GuardProfile),
                "Basic agent requires a guard profile.")
            .ValidateOnStart();
        services.TryAddSingleton<Tools.CurrentDateTimeTool>();
        services.TryAddSingleton<UserContextProvider>();
        services.TryAddSingleton<BasicAgent>();
        return services;
    }
}
