using MafPlayground.Studio.Catalog;
using MafPlayground.Studio.Mcp;
using MafPlayground.Studio.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MafPlayground.Studio.Http;

public static class StudioServiceExtensions
{
    public static IServiceCollection AddStudio(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // Binding failures must reach the pre-endpoint sanitizing middleware in every environment.
        services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        foreach (IConfigurationSection section in configuration.GetSection("AI:Entities").GetChildren().Prepend(configuration.GetSection("AI:Defaults")))
            if (!string.IsNullOrWhiteSpace(section["Model"])) _ = StudioProfileCatalog.ParseModel(section["Model"]);
        StudioOptions configured = configuration.GetSection("Studio:Limits").Get<StudioOptions>() ?? new();
        string[] secrets = configuration.AsEnumerable().Where(item => IsCredentialKey(item.Key) && !string.IsNullOrEmpty(item.Value))
            .Select(item => item.Value!).Concat(configured.Secrets).Distinct(StringComparer.Ordinal).ToArray();
        services.TryAddSingleton(provider => configured with
        { Secrets = secrets.Concat(provider.GetRequiredService<StudioAccessPolicy>().Secrets).ToArray() });
        services.AddStudioCatalog().AddStudioRuns();
        services.AddStudioMcp();
        services.TryAddSingleton(_ => new StudioAccessPolicy(configuration["Studio:Url"] ?? "http://localhost:5051"));
        services.TryAddSingleton<StudioSessionService>();
        services.TryAddScoped<StudioInspectionService>();
        services.TryAddSingleton<StudioProfileCatalog>();
        foreach (IConfigurationSection section in configuration.GetSection("Studio:Profiles").GetChildren())
        {
            string? id = section["Id"];
            if (!StudioProfileCatalog.IsSafeId(id)) throw new ArgumentException("Invalid configured Studio profile identity.");
            var profile = new StudioExecutionProfile(id!, StudioProfileCatalog.ParseModel(section["Model"]), section["GuardProfile"], section.GetValue<TimeSpan?>("ModelCallTimeout"));
            services.AddSingleton(profile);
        }
        // Resolve configured profile validation during startup, not on the first HTTP request.
        services.AddHostedService<StudioConfigurationValidator>();
        return services;
    }
    private static bool IsCredentialKey(string key)
    {
        string normalized = string.Concat(key.Split(':').Last().Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return normalized.Contains("apikey", StringComparison.Ordinal) || normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) || normalized.Contains("credential", StringComparison.Ordinal) ||
            normalized is "token" or "accesstoken" or "refreshtoken" or "authorization" or "cookie" or "privatekey" or "connectionstring" or "accountkey";
    }

    private sealed class StudioConfigurationValidator(StudioProfileCatalog profiles) : Microsoft.Extensions.Hosting.IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) { _ = profiles.Ids; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
