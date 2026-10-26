using System.Globalization;
using System.Text.Json;
using MafPlayground.Studio.Catalog;
using MafPlayground.Studio.Mcp;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static MafPlayground.Studio.Http.StudioAccessMiddleware;

namespace MafPlayground.Studio.Http;

public static class StudioEndpoints
{
    public const string Prefix = "/api/studio/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapStudio(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapStudioMcp();
        var root = endpoints.MapGroup("");
        root.MapGet("/studio/login", () => Results.Content("""
            <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Studio access</title>
            <h1>Local Agent Studio</h1><p>Enter the one-time code printed in your terminal.</p>
            <form method="post" action="/studio/login"><label>Access code <input name="code" type="password" required autocomplete="off" maxlength="64"></label><button>Open Studio</button></form></html>
            """, "text/html"));
        root.MapPost("/studio/login", async (HttpContext context, StudioAccessPolicy policy) =>
        {
            if (!string.Equals(context.Request.ContentType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) || context.Request.ContentLength > 1024) return Problem(context, 400, "invalid_form");
            IFormCollection form = await context.Request.ReadFormAsync(new FormOptions { ValueCountLimit = 1, KeyLengthLimit = 16, ValueLengthLimit = 64 }, context.RequestAborted);
            if (form["code"].Count != 1 || !policy.TryExchange(form["code"].ToString(), out string? cookie)) return Problem(context, 401, "invalid_code");
            context.Response.Cookies.Append(StudioAccessPolicy.CookieName, cookie!, new CookieOptions
            { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true });
            context.Response.Headers.Location = "/studio";
            return Results.StatusCode(303);
        }).DisableAntiforgery();
        root.MapGet("/studio", () => Results.Content("""
            <!doctype html><html lang="en"><meta charset="utf-8"><title>Local Agent Studio</title>
            <h1>Local Agent Studio</h1><p>The HTTP API is running. Frontend assets are not installed in this build.</p>
            <p>API: <a href="/api/studio/v1/entities">registered entities</a></p></html>
            """, "text/html"));
        var api = root.MapGroup(Prefix);
        api.MapGet("/entities", async (IStudioCatalog catalog, CancellationToken token) => Results.Ok(await catalog.ListAsync(token)));
        api.MapGet("/entities/{id}/topology", async (string id, string profileId, StudioInspectionService inspection, IStudioCatalog catalog, CancellationToken token) =>
        {
            inspection.ValidateEntity(id, profileId, false);
            return Results.Ok(await catalog.GetTopologyAsync(id, profileId, token));
        });
        api.MapGet("/profiles", (StudioProfileCatalog profiles) => Results.Ok(profiles.Ids));
        api.MapGet("/sessions", async (StudioSessionService sessions, CancellationToken token) => Results.Ok(await sessions.ListAsync(token)));
        api.MapPost("/sessions", async (HttpContext context, StudioInspectionService inspection, StudioSessionService sessions) =>
        {
            StudioSessionRequest request = await ReadAsync<StudioSessionRequest>(context);
            if (!inspection.ValidateEntity(request.EntityId, request.ProfileId, false).Descriptor.SupportsSessions) throw new ArgumentException();
            StudioSession session = await sessions.CreateAsync(request, context.RequestAborted);
            return Results.Created($"{Prefix}/sessions/{session.Id}", session);
        });
        api.MapPost("/sessions/{id}/reset", async (string id, StudioSessionService sessions, StudioAccessPolicy policy, CancellationToken token) =>
        Results.Ok(await sessions.ResetAndCreateAsync(id, policy.Caller, token)));
        api.MapPost("/runs", async (HttpContext context, StudioInspectionService inspection, StudioSessionService sessions, StudioAccessPolicy policy) =>
        {
            StudioRunRequest request = await ReadAsync<StudioRunRequest>(context);
            inspection.ValidateRun(request);
            StudioRunReceipt receipt = await sessions.StartAsync(request, policy.Caller, context.RequestAborted);
            return Results.Accepted($"{Prefix}/runs/{receipt.RunId}", receipt);
        });
        api.MapGet("/runs", async (IStudioRunService runs, CancellationToken token) => Results.Ok(await runs.ListAsync(token)));
        api.MapGet("/runs/{id}", async (string id, IStudioRunService runs, CancellationToken token) => Results.Ok(await RequiredAsync(runs, id, token)));
        api.MapGet("/runs/{id}/result", async (string id, IStudioRunService runs, CancellationToken token) =>
        {
            var run = await RequiredAsync(runs, id, token);
            if (!run.IsTerminal) throw new StudioResultPendingException();
            return Results.Ok(run);
        });
        api.MapPost("/runs/{id}/cancel", async (string id, IStudioRunService runs, CancellationToken token) =>
        {
            _ = await RequiredAsync(runs, id, token);
            if (!await runs.CancelAsync(id, token)) throw new StudioResultPendingException();
            return Results.Accepted($"{Prefix}/runs/{id}");
        });
        api.MapGet("/runs/{id}/payloads/{payloadId}", async (string id, string payloadId, StudioInspectionService inspection, CancellationToken token) =>
            Results.Ok(await inspection.GetPayloadAsync(id, payloadId, token) ?? throw new KeyNotFoundException()));
        api.MapGet("/runs/{id}/spans/{spanId}", async (string id, string spanId, StudioInspectionService inspection, CancellationToken token) =>
            Results.Ok(await inspection.GetSpanAsync(id, spanId, token) ?? throw new KeyNotFoundException()));
        api.MapGet("/runs/{id}/event-page", async (string id, HttpContext context, IStudioRunService runs) =>
        {
            long after = Cursor(context);
            int limit = context.Request.Query.TryGetValue("limit", out var value) ? int.Parse(value.ToString(), CultureInfo.InvariantCulture) : 100;
            return Results.Ok(await runs.ReadEventsAsync(id, after, limit, context.RequestAborted));
        });
        api.MapGet("/runs/{id}/events", StreamAsync);
        return endpoints;
    }

    private static async Task StreamAsync(string id, HttpContext context, IStudioRunService runs, IHostApplicationLifetime lifetime)
    {
        long after = Cursor(context);
        _ = await RequiredAsync(runs, id, context.RequestAborted);
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        CancellationToken token = watching.Token;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        try
        {
            await context.Response.StartAsync(token);
            await foreach (StudioEvent item in runs.WatchAsync(id, after, token))
            {
                // Flush each event directly. Slow clients retain no unbounded subscriber queue.
                await context.Response.WriteAsync($"id: {item.Sequence.ToString(CultureInfo.InvariantCulture)}\nevent: {SafeEventName(item.Kind)}\ndata: {JsonSerializer.Serialize(item, Json)}\n\n", token);
                await context.Response.Body.FlushAsync(token);
            }
        }
        catch (OperationCanceledException) when (watching.IsCancellationRequested) { }
    }

    private static string SafeEventName(string kind) => kind.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') ? kind : "capture";
    private static long Cursor(HttpContext context)
    {
        string value = context.Request.Query["after"].FirstOrDefault() ?? context.Request.Headers["Last-Event-ID"].FirstOrDefault() ?? "0";
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long after)) throw new ArgumentException();
        return after;
    }

    private static async ValueTask<T> ReadAsync<T>(HttpContext context)
    {
        // Also bounds chunked requests; Content-Length alone is not a security boundary.
        int maximum = context.RequestServices.GetRequiredService<StudioOptions>().MaxPayloadBytes + 4096;
        if (context.Request.ContentLength > maximum) throw new ArgumentException();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[Math.Min(maximum + 1, 8192)];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) != 0)
        {
            if (buffer.Length + read > maximum) throw new ArgumentException();
            await buffer.WriteAsync(chunk.AsMemory(0, read), context.RequestAborted);
        }
        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(buffer, Json, context.RequestAborted) ?? throw new ArgumentException();
    }

    private static async ValueTask<StudioRunSnapshot> RequiredAsync(IStudioRunService runs, string id, CancellationToken token) =>
        await runs.GetAsync(id, token) ?? throw new KeyNotFoundException();
    internal sealed class StudioResultPendingException : InvalidOperationException;
}
