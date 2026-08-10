using MafPlayground.AI;
using MafPlayground.AI.Contracts;
using MafPlayground.AI.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MafPlayground.Observability;

public static class ObservabilityServiceExtensions
{
    public static IServiceCollection AddMafPlaygroundObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(ObservabilityOptions.SectionName);
        services.AddOptions<ObservabilityOptions>().Bind(section);
        services.AddOptions<AgentTelemetryOptions>()
            .Bind(section.GetSection("AgentFramework"));

        ObservabilityOptions options = section.Get<ObservabilityOptions>() ?? new();
        if (!options.Enabled)
        {
            return services;
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            throw new InvalidOperationException(
                $"{ObservabilityOptions.SectionName}:ServiceName cannot be empty when observability is enabled.");
        }

        if (options.Cost.Enabled)
        {
            services.AddSingleton<IChatClientDecorator, CostTrackingChatClientDecorator>();
        }

        services.AddLogging(logging => logging.AddOpenTelemetry(openTelemetry =>
        {
            openTelemetry.IncludeFormattedMessage = true;
            openTelemetry.IncludeScopes = true;
            openTelemetry.AddOtlpExporter();
        }));

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(
                    AITelemetry.AgentSourceName,
                    AITelemetry.WorkflowSourceName,
                    ObservabilityTelemetry.TestHarnessSourceName)
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(
                    AITelemetry.AgentSourceName,
                    AITelemetry.OperationMeterName,
                    GuardTelemetry.MeterName,
                    ObservabilityTelemetry.CostMeterName)
                .AddOtlpExporter());

        return services;
    }
}
