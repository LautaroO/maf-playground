using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Content;

namespace MafPlayground.Tests.AI.Guards;

public sealed class GuardTelemetryTests
{
    [Fact]
    public async Task BlockedPii_EmitsSanitizedMetricAndActivityEvent()
    {
        const string secret = "private@example.com";
        ConcurrentQueue<KeyValuePair<string, object?>[]> measurements = new();
        using MeterListener meterListener = new();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == GuardTelemetry.MeterName &&
                instrument.Name == GuardTelemetry.DecisionMetricName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            measurements.Enqueue(tags.ToArray()));
        meterListener.Start();

        using Activity activity = new Activity("guard-telemetry-test").Start();
        ContentGuard guard = new(new RegexPiiContentInspector());

        await Assert.ThrowsAsync<ContentGuardRejectedException>(async () =>
            await guard.ApplyAsync(
                secret,
                GuardAction.Block,
                ContentOrigin.ToolArgument));

        KeyValuePair<string, object?>[] measurement = Assert.Single(
            measurements,
            tags => tags.Any(tag =>
                tag.Key == "maf_playground.guard.action" &&
                Equals(tag.Value, "block")));
        Assert.Contains(measurement, tag =>
            tag.Key == "maf_playground.guard.name" && Equals(tag.Value, "pii"));
        Assert.DoesNotContain(measurement, tag =>
            tag.Value?.ToString()?.Contains(secret, StringComparison.Ordinal) == true);

        ActivityEvent guardEvent = Assert.Single(
            activity.Events,
            item => item.Name == "ai.guard.content");
        Assert.DoesNotContain(guardEvent.Tags, tag =>
            tag.Value?.ToString()?.Contains(secret, StringComparison.Ordinal) == true);
    }
}
