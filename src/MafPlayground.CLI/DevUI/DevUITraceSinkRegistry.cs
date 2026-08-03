using System.Collections.Concurrent;
using System.Diagnostics;

namespace MafPlayground.CLI.DevUI;

internal sealed class DevUITraceSinkRegistry
{
    public static readonly object HttpContextItemKey = new();
    private readonly ConcurrentDictionary<ActivityTraceId, DevUITraceSink> _sinks = new();

    public IDisposable Register(ActivityTraceId traceId, DevUITraceSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (!_sinks.TryAdd(traceId, sink))
        {
            throw new InvalidOperationException($"A DevUI trace sink is already registered for trace '{traceId}'.");
        }

        return new Registration(_sinks, traceId, sink);
    }

    public void Capture(Activity activity, DevUITraceSink? currentRequestSink = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (currentRequestSink is not null)
        {
            currentRequestSink.Enqueue(DevUITraceActivity.FromActivity(activity));
        }
        else if (_sinks.TryGetValue(activity.TraceId, out DevUITraceSink? sink))
        {
            sink.Enqueue(DevUITraceActivity.FromActivity(activity));
        }
    }

    private sealed class Registration(
        ConcurrentDictionary<ActivityTraceId, DevUITraceSink> sinks,
        ActivityTraceId traceId,
        DevUITraceSink sink) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sinks.TryRemove(new KeyValuePair<ActivityTraceId, DevUITraceSink>(traceId, sink));
        }
    }
}
