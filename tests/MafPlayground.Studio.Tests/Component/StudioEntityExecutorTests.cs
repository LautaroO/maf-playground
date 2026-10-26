using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Abstractions;
using MafPlayground.Studio.Catalog;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Studio.Tests.Component;

public sealed class StudioEntityExecutorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownTypedContracts_ExecuteWithoutNativeSourceOrStatusGuessing(bool workflow)
    {
        ServiceCollection services = new();
        var builder = services.AddAIRuntime(new ConfigurationBuilder().Build());
        if (workflow) builder.AddWorkflow<UnknownEntity, UnknownInput, UnknownOutput>("unknown", Configure);
        else builder.AddAgent<UnknownEntity, UnknownInput, UnknownOutput>("unknown", Configure);
        services.AddStudioCatalog().AddStudioRuns();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IStudioEntityExecutor>();
        var request = new StudioRunRequest("request", "unknown", "cli-default", JsonSerializer.SerializeToElement(new { number = 7 }), null);
        var result = await executor.ExecuteAsync(request, new("run", "unknown", "cli-default", null, new("local")), CancellationToken.None);
        // Arbitrary domain fields must never imply execution failure.
        Assert.Equal(21, result.Output.GetProperty("value").GetInt32());
        Assert.Equal(StudioOutcomeClassification.Unclassified, result.Classification);
    }

    [Fact]
    public async Task ProfileExecution_DisposesItsModelClientAtScopeEnd()
    {
        await using StudioExecutionFixture fixture = new();
        await fixture.RunAsync(new { message = "hello" });
        Assert.Equal(1, fixture.Client.DisposeCount);
    }

    [Fact]
    public async Task BasicAgent_StreamsFinalOutputFromOneInvocation()
    {
        await using StudioExecutionFixture fixture = new();
        var result = await fixture.RunAsync(new { message = "hello" });
        Assert.Equal("hello world", result.Output.GetProperty("text").GetString());
        Assert.Single(fixture.Client.Requests);
        var deltas = fixture.Events.Items.Where(item => item.Kind == "text_delta");
        Assert.Equal("hello world", string.Concat(deltas.Select(item => item.Data.GetProperty("text").GetString())));
    }

    [Fact]
    public async Task MalformedInput_FailsBeforeModelCall()
    {
        await using StudioExecutionFixture fixture = new();
        await Assert.ThrowsAsync<AIEntityInputException>(() => fixture.RunAsync(new { message = 42 }));
        Assert.Empty(fixture.Client.Requests);
    }

    [Fact]
    public async Task BasicAgent_ExecutesTheRealDateToolAndReturnsItsValue()
    {
        await using StudioExecutionFixture fixture = new(services => services.AddSingleton<TimeProvider>(new FixedClock()));
        fixture.Client.Respond = (messages, _, _) =>
        {
            var tool = messages.SelectMany(message => message.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>().LastOrDefault();
            if (tool is null) return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant,
                [new Microsoft.Extensions.AI.FunctionCallContent("time-1", "get_current_date_time", new Dictionary<string, object?> { ["timeZoneId"] = "UTC" })])));
            string result = JsonSerializer.Serialize(tool.Result);
            Assert.Contains("2026-10-25", result);
            return Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, result)));
        };
        var response = await fixture.RunAsync(new { message = "what time is it?" });
        Assert.Contains("2026-10-25", response.Output.GetProperty("text").GetString());
        Assert.Equal(2, fixture.Client.Requests.Count);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-25T12:34:56Z");
    }

    [Fact]
    public async Task FailedRun_RetainsEffectiveProfileBeforeConfigurationChanges()
    {
        await using StudioExecutionFixture fixture = new();
        fixture.Client.Reply = (_, _) => throw new InvalidOperationException("controlled failure");
        StudioRunService runs = fixture.Provider.GetRequiredService<StudioRunService>();
        await runs.StartAsync(default);
        try
        {
            var receipt = await runs.StartAsync(new("failed-profile", "basic-agent", "cli-default", JsonSerializer.SerializeToElement(new { message = "hello" }), null), new("tester"), default);
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
            StudioRunSnapshot snapshot;
            do
            {
                snapshot = (await runs.GetAsync(receipt.RunId, deadline.Token))!;
                if (!snapshot.IsTerminal) await Task.Delay(1, deadline.Token);
            } while (!snapshot.IsTerminal);
            Assert.Equal(StudioRunStatus.Failed, snapshot.Status);
            Assert.NotNull(snapshot.Profile);
            Assert.Equal("fake:default", snapshot.Profile.Model);
            Assert.NotNull(snapshot.Profile.GuardProfile);
            Assert.True(snapshot.Profile.ModelCallTimeout > TimeSpan.Zero);
            fixture.Provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MafPlayground.AI.Runtime.Configuration.AIRuntimeOptions>>().Value.Defaults.Model = "fake:changed";
            Assert.Equal(snapshot.Profile, (await runs.GetAsync(receipt.RunId, default))!.Profile);
            Assert.Equal("fake:default", (await runs.GetAsync(receipt.RunId, default))!.Profile!.Model);
        }
        finally { await runs.StopAsync(default); }
    }

    [Fact]
    public async Task TranslationPartialOutcome_RemainsClassifiedWhenCaptureIsTruncated()
    {
        await using StudioExecutionFixture fixture = new(services => services.AddSingleton(new StudioOptions { MaxPayloadBytes = 300 }));
        fixture.Client.Reply = (messages, _) =>
        {
            string input = messages.Last().Text;
            if (input.Contains("\"fr\"", StringComparison.Ordinal)) throw new InvalidOperationException("controlled branch failure");
            return Task.FromResult(input.Contains("translatedText", StringComparison.OrdinalIgnoreCase)
                ? """{"isValid":true,"confidence":0.95,"issues":[]}""" : """{"translatedText":"Hola"}""");
        };
        StudioRunService runs = fixture.Provider.GetRequiredService<StudioRunService>();
        await runs.StartAsync(default);
        try
        {
            var receipt = await runs.StartAsync(new("partial", "translation-workflow", "cli-default",
                JsonSerializer.SerializeToElement(new { text = "Hello", targetLanguages = new[] { "es", "fr" } }), null), new("tester"), default);
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
            StudioRunSnapshot snapshot;
            do
            {
                snapshot = (await runs.GetAsync(receipt.RunId, deadline.Token))!;
                if (!snapshot.IsTerminal) await Task.Delay(1, deadline.Token);
            } while (!snapshot.IsTerminal);
            Assert.Equal(StudioRunStatus.PartiallyFailed, snapshot.Status);
            Assert.Equal(StudioOutcomeClassification.PartiallyFailed, snapshot.Outcome);
            Assert.False(snapshot.CaptureComplete);
        }
        finally { await runs.StopAsync(default); }
    }

    private static void Configure(MafPlayground.AI.Runtime.Registry.AIEntityRegistrationBuilder entity) =>
        entity.WithDescription("A dynamically discovered typed entity").AllowAnonymousAccess();

    public sealed record UnknownInput(int Number);
    public sealed record UnknownOutput(int Value, bool Success, bool IsValid);
    public sealed class UnknownEntity : IAIEntity<UnknownInput, UnknownOutput>
    {
        public ValueTask<UnknownOutput> ExecuteAsync(UnknownInput input, AIExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new UnknownOutput(input.Number * 3, false, false));
    }
}
