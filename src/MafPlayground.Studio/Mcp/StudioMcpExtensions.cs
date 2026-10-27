using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Mcp;

/// <summary>Official stateless Streamable HTTP transport over the existing Studio host services.</summary>
public static class StudioMcpExtensions
{
    public static IServiceCollection AddStudioMcp(this IServiceCollection services)
    {
        services.AddMcpServer().WithHttpTransport(options =>
        {
            options.Stateless = true;
            // Stateless mode disables server-to-client features and rejects legacy SSE.
        }).WithTools<StudioMcpTools>(JsonSerializerOptions.Web);
        return services;
    }

    public static IEndpointRouteBuilder MapStudioMcp(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp("/mcp");
        return endpoints;
    }
}
