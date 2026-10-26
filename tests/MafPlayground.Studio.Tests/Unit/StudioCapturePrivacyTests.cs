using System.Text.Json;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using MafPlayground.Studio.Tests.Component;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MafPlayground.AI.Configuration;

namespace MafPlayground.Studio.Tests.Unit;

public sealed class StudioCapturePrivacyTests
{
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
