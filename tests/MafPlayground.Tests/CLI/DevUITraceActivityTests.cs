using System.Diagnostics;
using System.Text;
using MafPlayground.AI;
using MafPlayground.CLI.DevUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MafPlayground.Tests;

public sealed class DevUITraceActivityTests
{
    [Fact]
    public void FromActivity_MapsHierarchyStatusAndAttributes()
    {
        using Activity parent = new("parent");
        parent.SetIdFormat(ActivityIdFormat.W3C);
        parent.Start();

        using Activity activity = new("execute_tool");
        activity.Start();
        activity.SetTag("gen_ai.tool.name", "get_current_date_time");
        activity.SetTag("attempt", 1);
        activity.SetStatus(ActivityStatusCode.Ok);
        activity.Stop();

        DevUITraceActivity result = DevUITraceActivity.FromActivity(activity);

        Assert.Equal(activity.TraceId.ToHexString(), result.TraceId);
        Assert.Equal(activity.SpanId.ToHexString(), result.SpanId);
        Assert.Equal(parent.SpanId.ToHexString(), result.ParentSpanId);
        Assert.Equal("execute_tool", result.OperationName);
        Assert.Equal("OK", result.Status);
        Assert.True(result.DurationMilliseconds >= 0);
        Assert.Equal("get_current_date_time", result.Attributes["gen_ai.tool.name"]);
        Assert.Equal(1, result.Attributes["attempt"]);
    }

    [Fact]
    public void FromActivity_MapsErrorStatusAndStableErrorType()
    {
        using Activity activity = new("invoke_agent basic-agent");
        activity.Start();
        activity.SetTag(
            AITelemetry.ErrorTypeTag,
            typeof(InvalidOperationException).FullName);
        activity.SetStatus(
            ActivityStatusCode.Error,
            typeof(InvalidOperationException).FullName);
        activity.Stop();

        DevUITraceActivity result = DevUITraceActivity.FromActivity(activity);

        Assert.Equal("ERROR", result.Status);
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            result.Attributes[AITelemetry.ErrorTypeTag]);
    }

    [Fact]
    public async Task ResponseStream_InsertsTraceEventBeforeNextResponseFrame()
    {
        await using MemoryStream output = new();
        DevUITraceSink sink = new("fallback-response");
        await using DevUITraceResponseStream stream = new(output, sink, () => true);
        byte[] created = Encoding.UTF8.GetBytes(
            "event: response.created\ndata: {\"response\":{\"id\":\"resp_123\",\"model\":\"basic-agent\"}}\n\n");
        await stream.WriteAsync(created);

        using Activity activity = new("invoke_agent basic-agent");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        activity.Stop();
        sink.Enqueue(DevUITraceActivity.FromActivity(activity));

        byte[] completed = Encoding.UTF8.GetBytes(
            "event: response.completed\ndata: {\"type\":\"response.completed\"}\n\n");
        await stream.WriteAsync(completed);
        await stream.FlushAsync();

        string response = Encoding.UTF8.GetString(output.ToArray());
        int traceIndex = response.IndexOf("event: response.trace.completed", StringComparison.Ordinal);
        int completedIndex = response.IndexOf("event: response.completed", StringComparison.Ordinal);
        Assert.True(traceIndex > 0);
        Assert.True(traceIndex < completedIndex);
        Assert.Contains("\"response_id\":\"resp_123\"", response, StringComparison.Ordinal);
        Assert.Contains("\"entity_id\":\"basic-agent\"", response, StringComparison.Ordinal);
        Assert.Contains("\"operation_name\":\"invoke_agent basic-agent\"", response, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_CapturesOnlyRegisteredTrace()
    {
        DevUITraceSinkRegistry registry = new();
        using Activity activity = new("registered");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        activity.Stop();
        DevUITraceSink sink = new("response");

        using (registry.Register(activity.TraceId, sink))
        {
            registry.Capture(activity);
        }

        Assert.True(sink.TryDequeueFrame(out byte[]? frame));
        Assert.Contains("registered", Encoding.UTF8.GetString(frame!), StringComparison.Ordinal);

        registry.Capture(activity);
        Assert.False(sink.TryDequeueFrame(out _));
    }

    [Fact]
    public async Task ServiceRegistration_ExportsMafActivitiesToRegisteredSink()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddDevUITracing();
        using IHost host = builder.Build();
        await host.StartAsync();
        DevUITraceSinkRegistry registry = host.Services
            .GetRequiredService<DevUITraceSinkRegistry>();

        using Activity parent = new("http-request");
        parent.SetIdFormat(ActivityIdFormat.W3C);
        parent.Start();
        DevUITraceSink sink = new("response");
        using IDisposable registration = registry.Register(parent.TraceId, sink);
        using ActivitySource source = new(AITelemetry.AgentSourceName);
        using Activity? activity = source.StartActivity("invoke_agent basic-agent");

        Assert.NotNull(activity);
        activity.Stop();
        Assert.True(sink.TryDequeueFrame(out _));

        await host.StopAsync();
    }
}
