using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.AI.AspNetCore;

public static class AIEndpointServiceExtensions
{
    public static IServiceCollection AddAIEndpoints(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AIEndpointOptions>()
            .Bind(configuration.GetSection(AIEndpointOptions.ConfigurationSectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.SubjectClaimType),
                "AI HTTP subject claim type is required.")
            .Validate(options => options.MaxRequestBodyBytes > 0,
                "AI HTTP request limit must be positive.")
            .ValidateOnStart();
        services.AddAuthorization();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AIAuthorizationStartupValidator>());
        return services;
    }
}
