using System.Text;
using System.Text.Json;
using MafPlayground.Studio.Capture;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioCaptureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedScalar_IsRejectedBeforeAllocatingProportionalSerializationBuffers(bool inEnumerable)
    {
        var store = new StudioPayloadStore(new StudioOptions { MaxPayloadBytes = 1024 });
        object warmup = inEnumerable ? new[] { new string('x', 2048) } : new string('x', 2048);
        for (int i = 0; i < 3; i++) store.Capture(warmup, "application/json");
        // Distinct pool size classes keep one theory case from hiding the other's allocation.
        string huge = new('x', (inEnumerable ? 16 : 8) * 1024 * 1024); // Preallocated input.
        object input = inEnumerable ? new[] { huge } : huge;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var payload = store.Capture(input, "application/json");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(StudioCaptureState.Truncated, payload.State);
        Assert.True(allocated < 256 * 1024, $"A 1 KiB capture allocated {allocated:N0} bytes for a preallocated {huge.Length:N0}-character scalar.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedUnicodeScalar_DoesNotAllocateAnUnboundedEscapingBuffer(bool inEnumerable)
    {
        var store = new StudioPayloadStore(new StudioOptions { MaxPayloadBytes = 1024 });
        object warmup = inEnumerable ? new[] { new string('☃', 2048) } : new string('☃', 2048);
        for (int i = 0; i < 3; i++) store.Capture(warmup, "text/plain");
        string huge = new('☃', (inEnumerable ? 16 : 8) * 1024 * 1024);
        object input = inEnumerable ? new[] { huge } : huge;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var payload = store.Capture(input, "text/plain");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(StudioCaptureState.Truncated, payload.State);
        Assert.True(allocated < 256 * 1024, $"A 1 KiB capture allocated {allocated:N0} bytes while escaping a preallocated Unicode scalar.");
    }

    [Fact]
    public void InBudgetUnicodeAndNestedJson_PreserveDefaultEscapingAndValidPayloads()
    {
        var store = new StudioPayloadStore(new StudioOptions { MaxPayloadBytes = 1024 });
        const string text = "Snow ☃ globe 🌍 quote \" newline\n<>&";
        var direct = store.Capture(text, "text/plain");
        Assert.Equal(StudioCaptureState.Available, direct.State);
        Assert.Equal(JsonSerializer.Serialize(text), direct.Value.GetRawText());
        var nested = store.Capture(new { text = JsonSerializer.Serialize(new { content = text }) }, "application/json");
        using var parsed = JsonDocument.Parse(nested.Value.GetProperty("text").GetString()!);
        Assert.Equal(text, parsed.RootElement.GetProperty("content").GetString());
        Assert.True(Encoding.UTF8.GetByteCount(nested.Value.GetRawText()) <= 1024);
    }

    [Fact]
    public void OversizedEnumerable_StopsAtTheWriterBudgetWithoutMaterializingTheSequence()
    {
        var store = new StudioPayloadStore(new StudioOptions { MaxPayloadBytes = 1024 });
        store.Capture(Enumerable.Repeat("small", 10000), "application/json");
        int visited = 0;
        IEnumerable<string> Values()
        {
            for (int i = 0; i < 1_000_000; i++) { visited++; yield return "small"; }
        }
        var values = Values();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var payload = store.Capture(values, "application/json");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(StudioCaptureState.Truncated, payload.State);
        Assert.True(visited < 10000, $"Capture enumerated {visited:N0} items after exceeding its 1 KiB budget.");
        Assert.True(allocated < 256 * 1024, $"Capture allocated {allocated:N0} bytes for a lazy sequence.");
    }

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
