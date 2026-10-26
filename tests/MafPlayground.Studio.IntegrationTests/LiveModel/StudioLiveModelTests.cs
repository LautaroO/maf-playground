using System.Collections.Concurrent;
using System.Text.Json;
using MafPlayground.AI;
using MafPlayground.AI.Context;
using MafPlayground.AI.Contracts;
using MafPlayground.CLI.Extensions;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using MafPlayground.Studio.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MafPlayground.Studio.IntegrationTests.LiveModel;

/// <summary>Opt-in execution smoke checks, not semantic evaluations or a model judge.</summary>
public sealed class StudioLiveModelTests
{
    [StudioLiveModelFact]
    [Trait("Category", "StudioLiveModel")]
    public async Task Agent_invokes_date_tool_and_retains_local_evidence()
    {
        await using var fixture = new LiveFixture();
        var result = await fixture.Execute("basic-agent", new { message = "Use get_current_date_time with timeZoneId UTC to tell me the current date and time. You must call the tool." });
        Assert.NotEqual(JsonValueKind.Null, result.Output.ValueKind);
        Assert.Contains(fixture.Events.Items, item => item.Kind == "tool_arguments");
        Assert.Contains(fixture.Events.Items, item => item.Kind == "tool_result");
        Assert.Contains(fixture.Events.Items, item => item.Kind == "model_request");
    }

    [StudioLiveModelFact]
    [Trait("Category", "StudioLiveModel")]
    public async Task Native_translation_produces_typed_result_and_correlated_events()
    {
        await using var fixture = new LiveFixture();
        var result = await fixture.Execute("translation-workflow", new { text = "Your order 247 is ready.", targetLanguages = new[] { "es" } });
        Assert.Equal(JsonValueKind.Object, result.Output.ValueKind);
        Assert.True(result.Output.GetProperty("translations").GetArrayLength() > 0);
        Assert.Contains(fixture.Events.Items, item => item.Kind == "model_request");
        Assert.Contains(fixture.Events.Items, item => item.ExecutorId is not null);
    }

    private sealed class LiveFixture : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        internal EventCollector Events { get; } = new();
        internal LiveFixture()
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            configuration["AI:Defaults:Model"] = Environment.GetEnvironmentVariable("AI_MODEL");
            // Local capture is independent of external telemetry; no exporter is registered here.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddConfiguredAIProviders(configuration);
            services.AddSingleton<IUserContextAccessor, AnonymousUser>();
            services.AddAIRuntime(configuration).AddMafPlaygroundSampleEntities();
            services.AddInMemoryAgentSessions();
            services.AddSingleton<IStudioEventSink>(Events);
            services.AddStudio(configuration);
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
        internal async Task<StudioEntityExecutionResult> Execute(string entity, object input)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await using var scope = provider.CreateAsyncScope();
            var request = new StudioRunRequest(Guid.NewGuid().ToString("N"), entity, "cli-default", JsonSerializer.SerializeToElement(input), null);
            return await scope.ServiceProvider.GetRequiredService<IStudioEntityExecutor>().ExecuteAsync(request,
                new(Guid.NewGuid().ToString("N"), entity, "cli-default", null, new("studio-live-smoke")), timeout.Token);
        }
        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
    private sealed class EventCollector : IStudioEventSink
    {
        internal ConcurrentQueue<StudioEvent> Items { get; } = new();
        public void Record(StudioEvent studioEvent) => Items.Enqueue(studioEvent);
    }
    private sealed class AnonymousUser : IUserContextAccessor { public UserContext GetCurrent() => new([]); }
    private sealed class StudioLiveModelFactAttribute : FactAttribute
    {
        public StudioLiveModelFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("RUN_STUDIO_LIVE_MODEL_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
                Skip = "Set RUN_STUDIO_LIVE_MODEL_TESTS=true and AI_MODEL=provider:model to opt into real provider smoke checks.";
            else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AI_MODEL")))
                Skip = "Studio live provider is unconfigured: AI_MODEL is required; no provider call was attempted.";
        }
    }
}
