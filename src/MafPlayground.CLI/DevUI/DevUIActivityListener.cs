using System.Diagnostics;
using MafPlayground.AI;
using MafPlayground.AI.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.CLI.DevUI;

internal sealed class DevUIActivityListener(
    DevUITraceSinkRegistry registry,
    IHttpContextAccessor httpContextAccessor) : IHostedService, IDisposable
{
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = source => source.Name is
            AITelemetry.AgentSourceName or AITelemetry.WorkflowSourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
            ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = activity =>
        {
            DevUITraceSink? currentRequestSink = httpContextAccessor.HttpContext?
                .Items[DevUITraceSinkRegistry.HttpContextItemKey] as DevUITraceSink;
            registry.Capture(activity, currentRequestSink);
        },
    };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _listener.Dispose();
}
