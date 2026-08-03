using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace MafPlayground.CLI.DevUI;

internal sealed class DevUITraceMiddleware(
    RequestDelegate next,
    DevUITraceSinkRegistry registry)
{
    public async Task InvokeAsync(HttpContext context)
    {
        Activity? requestActivity = Activity.Current;
        if (requestActivity is null || !IsResponsesRequest(context.Request))
        {
            await next(context);
            return;
        }

        DevUITraceSink sink = new(requestActivity.TraceId.ToHexString());
        using IDisposable registration = registry.Register(requestActivity.TraceId, sink);
        context.Items[DevUITraceSinkRegistry.HttpContextItemKey] = sink;
        Stream originalBody = context.Response.Body;
        using DevUITraceResponseStream traceBody = new(
            originalBody,
            sink,
            () => context.Response.ContentType?.StartsWith(
                "text/event-stream",
                StringComparison.OrdinalIgnoreCase) == true);
        context.Response.Body = traceBody;

        try
        {
            await next(context);
            await traceBody.DrainAsync(context.RequestAborted);
        }
        finally
        {
            context.Items.Remove(DevUITraceSinkRegistry.HttpContextItemKey);
            context.Response.Body = originalBody;
        }
    }

    private static bool IsResponsesRequest(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) &&
        (request.Path.StartsWithSegments("/v1/responses") ||
         request.Path.Value?.EndsWith("/v1/responses", StringComparison.OrdinalIgnoreCase) == true);
}
