using System.Text;
using System.Text.Json;
using MafPlayground.Studio.Capture;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioCaptureTests
{
    [Fact]
    public void Payloads_BoundUnicodeAndDepth_AndContainSerializationFailures()
    {
        var store = new StudioPayloadStore(new StudioOptions());
        var large = store.Capture(string.Concat(Enumerable.Repeat("🌍", 600000)), "text/plain");
        Assert.Equal(StudioCaptureState.Truncated, large.State);
        Assert.True(Encoding.UTF8.GetByteCount(large.Value.GetRawText()) <= 2 * 1024 * 1024);
        Assert.DoesNotContain("�", large.Value.ToString());
        var malformed = store.Capture(new Broken(), "application/json");
        Assert.Equal(StudioCaptureState.Unavailable, malformed.State);
        Assert.Equal("serialization_failed", malformed.Reason);
        object deep = "leaf";
        for (int i = 0; i < 100; i++) deep = new { nested = deep };
        Assert.Equal(StudioCaptureState.Unavailable, store.Capture(deep, "application/json").State);
    }

    [Fact]
    public void ConfiguredSecretsAndNestedCredentialFields_AreRedacted()
    {
        var store = new StudioPayloadStore(new StudioOptions { Secrets = ["secret-value"] });
        var payload = store.Capture(new { message = "contains secret-value", nested = new { Access_Token = "hidden" }, text = "Authorization: Bearer opaque-token" }, "application/json");
        Assert.Equal(StudioCaptureState.Redacted, payload.State);
        Assert.DoesNotContain("secret-value", payload.Value.ToString());
        Assert.DoesNotContain("hidden", payload.Value.ToString());
        Assert.DoesNotContain("opaque-token", payload.Value.ToString());
        Assert.Contains("contains", payload.Value.ToString());
    }
    [Fact]
    public void MalformedEmbeddedJson_DoesNotBypassCredentialRedaction()
    {
        var store = new StudioPayloadStore(new());
        var captured = store.Capture(new { message = "{\"password\":\"embedded-secret\"" }, "application/json");
        Assert.DoesNotContain("embedded-secret", captured.Value.ToString());
        Assert.NotEqual(StudioCaptureState.Available, captured.State);
    }

    [Fact]
    public async Task TruncatedObservedPayload_MarksCaptureIncomplete_AndLateEventsRemainRetained()
    {
        await using RunHarness harness = await RunHarness.CreateAsync();
        var receipt = await harness.SubmitAsync("truncated-observation");
        var execution = await harness.NextAsync();
        using (var capture = new StudioCaptureScope(execution.Context, harness.Service, new StudioPayloadStore(new()), harness.Clock))
            capture.Record("model_response", new string('x', 3 * 1024 * 1024));
        execution.Complete();
        var finished = await harness.TerminalAsync(receipt.RunId);
        Assert.False(finished.CaptureComplete);
        harness.Record(receipt.RunId, new { late = true });
        var after = await harness.Service.GetAsync(receipt.RunId, default);
        Assert.True(after!.LatestSequence > finished.LatestSequence);
        Assert.Equal(StudioRunStatus.Completed, after.Status);
    }

    private sealed class Broken { public string Value => throw new InvalidOperationException("secret-value"); }
}
