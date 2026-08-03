using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.CLI.DevUI;

internal static class DevUITracingExtensions
{
    public static IServiceCollection AddDevUITracing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddSingleton<DevUITraceSinkRegistry>();
        services.AddSingleton<DevUIActivityListener>();
        services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<DevUIActivityListener>());
        return services;
    }

    public static IApplicationBuilder UseDevUITracing(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<DevUITraceMiddleware>();
    }
}
