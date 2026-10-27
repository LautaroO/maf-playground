using System.Text.Json;
using MafPlayground.AI.Abstractions;
using MafPlayground.Studio.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Http;

/// <summary>Install before endpoints and static files in the dedicated Studio host.</summary>
public static class StudioAccessMiddleware
{
    /// <summary>Protects all downstream routes, including binding, service activation and result execution.</summary>
    public static IApplicationBuilder UseStudioAccess(this IApplicationBuilder app)
    {
        // Resolve immutable host policy at composition time; do not defer its validation to a request.
        StudioAccessPolicy policy = app.ApplicationServices.GetRequiredService<StudioAccessPolicy>();
        return app.Use(async (context, next) =>
        {
            ApplyHeaders(context);
            try
            {
                if (!policy.IsLocalRequest(context))
                {
                    await Problem(context, 403, "local_access_required").ExecuteAsync(context);
                    return;
                }
                if (context.Request.Path != "/studio/login" && !(context.Request.Path.StartsWithSegments("/mcp") ? policy.IsBearerAuthenticated(context) : policy.IsAuthenticated(context)))
                {
                    await Problem(context, 401, "authentication_required").ExecuteAsync(context);
                    return;
                }
                if (context.Request.Path.StartsWithSegments("/mcp") && HttpMethods.IsPost(context.Request.Method))
                {
                    // Bound chunked JSON-RPC before SDK parsing/argument binding as well as run admission.
                    int maximum = context.RequestServices.GetRequiredService<StudioOptions>().MaxPayloadBytes + 4096;
                    if (context.Request.ContentLength > maximum) throw new ArgumentException();
                    Stream original = context.Request.Body;
                    using var bounded = new MemoryStream();
                    byte[] chunk = new byte[Math.Min(maximum + 1, 8192)];
                    int read;
                    while ((read = await original.ReadAsync(chunk, context.RequestAborted)) != 0)
                    {
                        if (bounded.Length + read > maximum) throw new ArgumentException();
                        await bounded.WriteAsync(chunk.AsMemory(0, read), context.RequestAborted);
                    }
                    bounded.Position = 0;
                    context.Request.Body = bounded;
                    try { await next(context); }
                    finally { context.Request.Body = original; }
                }
                else await next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (context.Response.HasStarted)
                {
                    // A partial response cannot become a problem. Do not pass private diagnostics to development middleware.
                    context.Abort();
                    return;
                }
                (int status, string code) = exception switch
                {
                    AIEntityNotFoundException or KeyNotFoundException => (404, "not_found_or_evicted"),
                    StudioEntityForbiddenException => (403, "entity_forbidden"),
                    StudioCapacityException => (429, "capacity_exceeded"),
                    StudioSessionBusyException or StudioSessionConflictException or StudioRequestConflictException or AISessionConcurrencyException or StudioEntityUnavailableException or StudioEndpoints.StudioResultPendingException => (409, "state_conflict"),
                    ArgumentException or JsonException or InvalidDataException or FormatException or OverflowException or BadHttpRequestException => (400, "invalid_request"),
                    _ => (500, "operation_failed"),
                };
                context.Response.Clear();
                ApplyHeaders(context);
                await Problem(context, status, code).ExecuteAsync(context);
            }
        });
    }

    internal static IResult Problem(HttpContext context, int status, string code) => Results.Problem(statusCode: status,
        title: code, extensions: new Dictionary<string, object?>
        {
            ["correlationId"] = context.TraceIdentifier,
            ["runId"] = context.Request.Path.StartsWithSegments(StudioEndpoints.Prefix + "/runs") &&
                context.Request.RouteValues["id"] is string id && StudioProfileCatalog.IsSafeId(id) ? id : null,
        });

    private static void ApplyHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; worker-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
    }
}
