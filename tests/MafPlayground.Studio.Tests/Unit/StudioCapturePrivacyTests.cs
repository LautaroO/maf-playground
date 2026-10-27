using System.Text.Json;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Capture;
using MafPlayground.Studio.Execution;
using MafPlayground.Studio.Tests.Component;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MafPlayground.AI.Configuration;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioCapturePrivacyTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(503)]
    public void Wrapped_provider_failures_preserve_safe_cause_types_and_http_status_only(int status)
    {
        Exception cause = new HttpRequestException(
            "https://private.example/?key=hidden-provider-value response: confidential", null,
            (System.Net.HttpStatusCode)status);
        cause.Data["responseBody"] = "confidential-provider-body";
        Exception failure = new MafPlayground.AI.Abstractions.AIProviderFailureException(
            "The AI provider call failed.", new InvalidOperationException("private-inner-message", cause));

        var payload = new StudioPayloadStore(new()).Capture(failure, "application/json");

        Assert.Equal(StudioCaptureState.Available, payload.State);
        Assert.Equal("The AI provider call failed.", payload.Value.GetProperty("message").GetString());
        JsonElement[] causes = payload.Value.GetProperty("causes").EnumerateArray().ToArray();
        Assert.Equal(typeof(InvalidOperationException).FullName, causes[0].GetProperty("errorType").GetString());
        Assert.False(causes[0].TryGetProperty("statusCode", out _));
        Assert.Equal(typeof(HttpRequestException).FullName, causes[1].GetProperty("errorType").GetString());
        Assert.Equal(status, causes[1].GetProperty("statusCode").GetInt32());
        Assert.All(causes, item => Assert.False(item.TryGetProperty("message", out _)));
        string captured = payload.Value.ToString();
        Assert.DoesNotContain("private.example", captured);
        Assert.DoesNotContain("hidden-provider-value", captured);
        Assert.DoesNotContain("confidential", captured);
        Assert.DoesNotContain("private-inner-message", captured);
        Assert.DoesNotContain("stackTrace", captured, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provider_neutral_status_metadata_is_captured_without_raw_provider_details()
    {
        var failure = new MafPlayground.AI.Abstractions.AIProviderFailureException(
            "The AI provider call failed.", new InvalidOperationException("private provider response"))
        { Details = new MafPlayground.AI.Abstractions.AIProviderFailureDetails(429) };
        var payload = new StudioPayloadStore(new()).Capture(failure, "application/json");

        Assert.Equal(429, payload.Value.GetProperty("statusCode").GetInt32());
        Assert.Equal("The AI provider call failed.", payload.Value.GetProperty("message").GetString());
        Assert.DoesNotContain("private provider response", payload.Value.ToString());
    }

    [Fact]
    public async Task Model_failure_events_preserve_sanitized_http_causes_through_runtime_wrapping()
    {
        await using StudioExecutionFixture fixture = new();
        fixture.Client.Reply = (_, _) => throw new HttpRequestException(
            "private-provider-body", null, System.Net.HttpStatusCode.TooManyRequests);

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.RunAsync(new { message = "hello" }));

        StudioEvent failure = Assert.Single(fixture.Events.Items, item => item.Kind == "model_failed");
        JsonElement details = failure.Data.GetProperty("Value");
        Assert.Equal("The AI provider streaming call failed.", details.GetProperty("message").GetString());
        Assert.Equal(429, Assert.Single(details.GetProperty("causes").EnumerateArray()).GetProperty("statusCode").GetInt32());
        Assert.DoesNotContain("private-provider-body", JsonSerializer.Serialize(fixture.Events.Items));
    }

    [Fact]
    public void Exception_cause_capture_is_bounded_and_does_not_read_inner_messages()
    {
        Exception cause = new BrokenException();
        for (int index = 0; index < 12; index++) cause = new InvalidOperationException("hidden", cause);
        var payload = new StudioPayloadStore(new()).Capture(new Exception("safe", cause), "application/json");

        Assert.Equal(StudioCaptureState.Available, payload.State);
        Assert.Equal(8, payload.Value.GetProperty("causes").GetArrayLength());
        Assert.True(payload.Value.GetProperty("causesTruncated").GetBoolean());
        var broken = new StudioPayloadStore(new()).Capture(new Exception("safe", new BrokenException()), "application/json");
        Assert.Equal(StudioCaptureState.Available, broken.State);
        Assert.Single(broken.Value.GetProperty("causes").EnumerateArray());
    }

    [Fact]
    public void Benign_timestamps_are_available_even_when_json_writer_escapes_the_offset()
    {
        var value = new { StartedAt = DateTimeOffset.Parse("2026-10-25T12:30:00+00:00"), note = "C++ and <text>" };
        var payload = new StudioPayloadStore(new()).Capture(value, "application/json");
        Assert.Equal(StudioCaptureState.Available, payload.State);
        Assert.Null(payload.Reason);
        Assert.Equal(value.StartedAt, payload.Value.GetProperty("StartedAt").GetDateTimeOffset());
        Assert.Equal(value.note, payload.Value.GetProperty("note").GetString());
    }
    [Fact]
    public void Benign_embedded_json_preserves_text_formatting_while_nested_credentials_are_still_redacted()
    {
        const string benign = "  { \"count\": 2, \"text\": \"a+b\" }  ";
        var store = new StudioPayloadStore(new());
        var payload = store.Capture(new { data = benign }, "application/json");
        Assert.Equal(StudioCaptureState.Available, payload.State);
        Assert.Equal(benign, payload.Value.GetProperty("data").GetString());
        var sensitive = store.Capture(new { data = "  { \"password\": \"private-fixture-value\" } " }, "application/json");
        Assert.Equal(StudioCaptureState.Redacted, sensitive.State);
        Assert.DoesNotContain("private-fixture-value", sensitive.Value.ToString());
    }
    [Theory]
    [InlineData("password=\"quoted-secret\"", "quoted-secret")]
    [InlineData("Failure details: {\"password\":\"prefixed-secret\"}", "prefixed-secret")]
    [InlineData("Authorization: Basic dXNlcjpwYXNz", "dXNlcjpwYXNz")]
    [InlineData("Failure details: {\"token\":\"prefixed-token-secret\"}", "prefixed-token-secret")]
    [InlineData("Failure details: {\"privateKey\":\"prefixed-private-key-secret\"}", "prefixed-private-key-secret")]
    [InlineData("Failure details: {\"connectionString\":\"prefixed-connection-secret\"}", "prefixed-connection-secret")]
    [InlineData("password=unquoted-secret", "unquoted-secret")]
    [InlineData("password='single-quoted secret'", "single-quoted secret")]
    [InlineData("Failure details: {\"api_key\":\"unfinished-secret", "unfinished-secret")]
    [InlineData("password=\"unterminated-secret\\", "unterminated-secret")]
    [InlineData("password='unterminated-single\\", "unterminated-single")]
    [InlineData("Failure details: privateKey=first-secret second-secret", "first-secret second-secret")]
    public Task CredentialText_IsRedactedAtPayloadAndStoredErrorJournalBoundaries(string original, string secret) => AssertCredentialTextAsync(original, secret);

    private static async Task AssertCredentialTextAsync(string original, string secret)
    {
        var payload = new StudioPayloadStore(new()).Capture(new { message = original }, "application/json");
        foreach (string fragment in secret.Split(' ')) Assert.DoesNotContain(fragment, payload.Value.ToString());
        Assert.Equal(StudioCaptureState.Redacted, payload.State);
        await using RunHarness harness = await RunHarness.CreateAsync();
        var receipt = await harness.Service.StartAsync(RunHarness.Request("credential-text") with
        { Input = JsonSerializer.SerializeToElement(new { message = original }) }, RunHarness.Caller, default);
        var execution = await harness.NextAsync();
        Assert.Equal(original, execution.Request.Input.GetProperty("message").GetString());
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(await harness.Service.GetAsync(receipt.RunId, default)));
        harness.Record(receipt.RunId, new { message = original });
        execution.Completion.TrySetException(new InvalidOperationException(original));
        var snapshot = await harness.TerminalAsync(receipt.RunId);
        var events = await harness.Service.ReadEventsAsync(receipt.RunId, 0, 100, default);
        Assert.Equal(StudioRunStatus.Failed, snapshot.Status);
        foreach (string fragment in secret.Split(' ')) Assert.DoesNotContain(fragment, JsonSerializer.Serialize(new { snapshot, events }));
        Assert.Contains(events.Events, item => item.Kind == "execution_error" && item.CaptureState == StudioCaptureState.Redacted);
    }

    public static IEnumerable<object[]> CredentialFieldNames => new[]
    {
        "apiKey", "provider.api_key", "databasePassword", "clientSecret", "credentialBundle",
        "token", "access_token", "refresh-token", "Authorization", "Proxy-Authorization",
        "Cookie", "Set-Cookie", "privateKey", "connectionString", "configuration", "AccountKey", "SharedAccessSignature",
    }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(CredentialFieldNames))]
    public async Task PrefixedCredentialFields_UseTheSamePolicyAsStructuredFields(string field)
    {
        const string secret = "policy-value-must-disappear";
        var store = new StudioPayloadStore(new());
        var structured = store.Capture(new Dictionary<string, object?> { [field] = secret }, "application/json");
        string fragment = JsonSerializer.Serialize(new Dictionary<string, object?> { [field] = secret });
        await AssertCredentialTextAsync("Failure details: " + fragment, secret);
        Assert.DoesNotContain(secret, structured.Value.ToString());
        Assert.Equal(StudioCaptureState.Redacted, structured.State);
    }

    [Fact]
    public async Task SubmittedInputAndResult_AreRedactedBeforeInspection_ExecutionReceivesOriginal()
    {
        await using RunHarness harness = await RunHarness.CreateAsync();
        var request = RunHarness.Request("private") with { Input = JsonSerializer.SerializeToElement(new { apiKey = "submission-secret", message = "hello" }) };
        var receipt = await harness.Service.StartAsync(request, RunHarness.Caller, default);
        var execution = await harness.NextAsync();
        Assert.Equal("submission-secret", execution.Request.Input.GetProperty("apiKey").GetString());
        Assert.DoesNotContain("submission-secret", JsonSerializer.Serialize(await harness.Service.GetAsync(receipt.RunId, default)));
        harness.Record(receipt.RunId, new { Authorization = "Bearer event-secret", content = "hello" });
        execution.Complete(JsonSerializer.SerializeToElement(new { password = "result-secret", answer = "hello" }));
        var snapshot = await harness.TerminalAsync(receipt.RunId);
        Assert.DoesNotContain("result-secret", JsonSerializer.Serialize(snapshot));
        Assert.Equal("hello", snapshot.Result!.Value.GetProperty("answer").GetString());
        var events = await harness.Service.ReadEventsAsync(receipt.RunId, 0, 100, default);
        Assert.DoesNotContain("event-secret", JsonSerializer.Serialize(events));
    }

    [Fact]
    public async Task ErrorsAndConfiguredSecrets_AreSafeInStoredSnapshotsAndJournal()
    {
        await using RunHarness harness = await RunHarness.CreateAsync(options => options with { Secrets = ["configured-value"] });
        var request = RunHarness.Request("errors-configured-value") with { Input = JsonSerializer.SerializeToElement(new { message = "configured-value" }) };
        var receipt = await harness.Service.StartAsync(request, RunHarness.Caller, default);
        var execution = await harness.NextAsync();
        execution.Completion.TrySetException(new InvalidOperationException("password=unlisted-secret configured-value"));
        var snapshot = await harness.TerminalAsync(receipt.RunId);
        var events = await harness.Service.ReadEventsAsync(receipt.RunId, 0, 100, default);
        string captured = JsonSerializer.Serialize(new { snapshot, events });
        Assert.DoesNotContain("configured-value", captured);
        Assert.DoesNotContain("unlisted-secret", captured);
        Assert.Equal(StudioRunStatus.Failed, snapshot.Status);
        Assert.Contains(events.Events, item => item.Kind == "execution_error");
    }

    [Fact]
    public async Task MalformedExceptionDetails_CannotPreventTerminalPublication()
    {
        await using RunHarness harness = await RunHarness.CreateAsync();
        var receipt = await harness.SubmitAsync("malformed-error");
        var execution = await harness.NextAsync();
        execution.Completion.TrySetException(new BrokenException());
        Assert.Equal(StudioRunStatus.Failed, (await harness.TerminalAsync(receipt.RunId)).Status);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawExecutionEnvelope_IsReleasedForCompletedAndQueuedCancelledRuns(bool cancelQueued)
    {
        await using RunHarness harness = await RunHarness.CreateAsync(options => options with { MaxConcurrentRuns = 1 });
        var blocker = await harness.SubmitAsync("blocker");
        var active = await harness.NextAsync();
        var receipt = cancelQueued ? await harness.SubmitAsync("queued-private") : blocker;
        object store = typeof(StudioRunService).GetField("store", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(harness.Service)!;
        object state = store.GetType().GetMethod("Find", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(store, [receipt.RunId])!;
        var requestProperty = state.GetType().GetProperty("ExecutionRequest", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.NotNull(requestProperty.GetValue(state));
        if (cancelQueued) Assert.True(await harness.Service.CancelAsync(receipt.RunId, default));
        active.Complete();
        var terminal = await harness.TerminalAsync(receipt.RunId);
        Assert.Equal(cancelQueued ? StudioRunStatus.Cancelled : StudioRunStatus.Completed, terminal.Status);
        Assert.Null(requestProperty.GetValue(state));
    }
    [Fact]
    public async Task EffectiveProfileSnapshot_RedactsConfiguredSecretWithoutChangingModelSelection()
    {
        await using StudioExecutionFixture fixture = new(services =>
        {
            services.RemoveAll<StudioOptions>();
            services.AddSingleton(new StudioOptions { Secrets = ["configured-model"] });
            services.AddSingleton(new StudioExecutionProfile("private-profile", AIModelSelection.Parse("fake:configured-model")));
        });
        var service = fixture.Provider.GetRequiredService<StudioRunService>();
        await service.StartAsync(default);
        try
        {
            var receipt = await service.StartAsync(RunHarness.Request("profile-private") with
            { EntityId = "basic-agent", ProfileId = "private-profile", Input = JsonSerializer.SerializeToElement(new { message = "hello" }) }, RunHarness.Caller, default);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            StudioRunSnapshot snapshot;
            do
            {
                snapshot = (await service.GetAsync(receipt.RunId, deadline.Token))!;
                if (!snapshot.IsTerminal) await Task.Delay(1, deadline.Token);
            } while (!snapshot.IsTerminal);
            Assert.Equal(StudioRunStatus.Completed, snapshot.Status);
            Assert.Equal("configured-model", Assert.Single(fixture.Client.Models));
            Assert.DoesNotContain("configured-model", JsonSerializer.Serialize(snapshot));
            Assert.Contains("[REDACTED]", snapshot.Profile!.Model);
        }
        finally { await service.StopAsync(default); }
    }
    private sealed class BrokenException : Exception { public override string Message => throw new InvalidOperationException("getter failed"); }
}
