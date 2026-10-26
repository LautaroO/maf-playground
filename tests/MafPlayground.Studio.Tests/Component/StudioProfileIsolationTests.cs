using System.Collections.Concurrent;
using System.Text.Json;
using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.AI.Configuration;
using MafPlayground.AI.Contracts;
using MafPlayground.AI.Runtime.Registry;
using MafPlayground.AI.Workflows.Translation;
using MafPlayground.Retrieval;
using MafPlayground.Studio.Catalog;
using MafPlayground.Studio.Contracts;
using MafPlayground.Studio.Execution;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MafPlayground.Studio.Tests.Component;

public sealed class StudioProfileIsolationTests
{
    [Fact]
    public async Task ProfileGuards_AreAppliedWithoutMutatingDefaultOptions()
    {
        await using StudioExecutionFixture fixture = new(services =>
        {
            services.Configure<MafPlayground.AI.Guards.AIGuardOptions>(options => options.Profiles["redact"] = new()
            {
                Content = new() { Enabled = true, InputAction = MafPlayground.AI.Guards.GuardAction.Redact, OutputAction = MafPlayground.AI.Guards.GuardAction.Redact },
            });
            services.AddSingleton(new StudioExecutionProfile("redact", GuardProfile: "redact"));
        });
        fixture.Client.Reply = (_, _) => Task.FromResult("Contact support@example.com");
        var redacted = await fixture.RunAsync(new { message = "Email person@example.com" }, profile: "redact");
        var plain = await fixture.RunAsync(new { message = "Email person@example.com" });
        Assert.DoesNotContain("support@example.com", redacted.Output.ToString());
        Assert.Contains("support@example.com", plain.Output.ToString());
        Assert.DoesNotContain("person@example.com", string.Join(" ", fixture.Client.Requests.First().Select(message => message.Text)));
        Assert.Contains("person@example.com", string.Join(" ", fixture.Client.Requests.Last().Select(message => message.Text)));
    }

    [Fact]
    public async Task NativeDefaultSources_RemainUsableAtRootForDevUI()
    {
        await using StudioExecutionFixture fixture = Create(new(), new(), services => services
            .AddSingleton<IChatClient>(new StudioExecutionFixture.RecordingClient())
            .AddBasicRagAgent()
            .AddHelpAgent(new("host-help", "Host help", "Explain this host using its capabilities.")));
        await using var scope = fixture.Provider.CreateAsyncScope();
        foreach (string id in new[] { "basic-rag-agent", "repository-help-agent" })
        {
            var registration = scope.ServiceProvider.GetRequiredService<IAIEntityInspection>().GetRegistration(id);
            Assert.NotNull(registration.AgentSourceFactory!(fixture.Provider, new("host-default")).Agent);
        }
    }

    [Theory]
    [InlineData("basic-rag-agent", "AddBasicRagAgent")]
    [InlineData("repository-help-agent", "AddHelpAgent")]
    public async Task UnboundGroundedSources_ReportMissingHostRegistrationExplicitly(string id, string registration)
    {
        ProfileProvider clients = new();
        await using StudioExecutionFixture fixture = Create(clients, new());
        Assert.Null(fixture.Provider.GetService<IChatClient>());
        await using var scope = fixture.Provider.CreateAsyncScope();
        var inspection = scope.ServiceProvider.GetRequiredService<IAIEntityInspection>();
        var source = inspection.GetAgentSource(id, new("host-default"));
        InvalidOperationException nativeError = Assert.Throws<InvalidOperationException>(() => source!.Agent);
        Assert.Contains(registration, nativeError.Message);
        InvalidOperationException typedError = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAIEntityInvoker>()
            .InvokeAsync(id, JsonSerializer.SerializeToElement(new { message = "hello" }), AIExecutionContext.Create(new("tester")), default).AsTask());
        Assert.Equal(nativeError.Message, typedError.Message);
        Assert.Empty(clients.Created);
    }

    [Theory]
    [InlineData("basic-rag-agent")]
    [InlineData("repository-help-agent")]
    public async Task GroundedInvalidInput_StopsBeforeConstructingAClient(string entity)
    {
        ProfileProvider clients = new();
        await using StudioExecutionFixture fixture = Create(clients, new());
        await Assert.ThrowsAsync<AIEntityInputException>(() => fixture.RunAsync(new { message = 42 }, entity, "a"));
        Assert.Empty(clients.Created);
    }

    [Fact]
    public async Task ExplicitProfile_DoesNotRequireAConfiguredDefaultModel()
    {
        await using StudioExecutionFixture fixture = new(services =>
        {
            services.Configure<MafPlayground.AI.Runtime.Configuration.AIRuntimeOptions>(options => options.Defaults.Model = null);
            services.AddSingleton(new StudioExecutionProfile("explicit", AIModelSelection.Parse("fake:chosen")));
        });
        var result = await fixture.RunAsync(new { message = "hello" }, profile: "explicit");
        Assert.Equal("hello world", result.Output.GetProperty("text").GetString());
        Assert.Equal("chosen", Assert.Single(fixture.Client.Models));
    }

    [Fact]
    public async Task NativeAgentInspection_DoesNotCreateClientsAndRejectsScopeProfileMismatch()
    {
        ProfileProvider clients = new();
        await using StudioExecutionFixture fixture = Create(clients, new());
        await using var scope = fixture.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IStudioCatalog>().GetTopologyAsync("basic-agent", "a", default);
        var inspection = scope.ServiceProvider.GetRequiredService<IAIEntityInspection>();
        Assert.NotNull(inspection.GetAgentSource("basic-agent", new("a")));
        Assert.Empty(clients.Created);
        Assert.Throws<InvalidOperationException>(() => inspection.GetAgentSource("basic-agent", new("b")));
    }

    [Fact]
    public async Task ConcurrentRagProfiles_KeepRepairClientsAndKnowledgeBasesIsolated()
    {
        ProfileProvider clients = new();
        SearchFactory search = new();
        TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0;
        foreach (string model in new[] { "a", "b" })
        {
            var client = clients.Clients[model];
            int calls = 0;
            client.Reply = async (_, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    if (Interlocked.Increment(ref entered) == 2) both.TrySetResult();
                    await both.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                    return JsonSerializer.Serialize(new { insufficientEvidence = false, claims = new[] { new { text = $"Answer {model}", citationIds = new[] { "invalid" } } } });
                }
                return JsonSerializer.Serialize(new { insufficientEvidence = false, claims = new[] { new { text = $"Answer {model}", citationIds = new[] { "e1" } } } });
            };
        }
        await using StudioExecutionFixture fixture = Create(clients, search);
        Assert.Null(fixture.Provider.GetService<IChatClient>());
        var results = await Task.WhenAll(fixture.RunAsync(new BasicRagAgentRequest("question a"), "basic-rag-agent", "a"),
            fixture.RunAsync(new BasicRagAgentRequest("question b"), "basic-rag-agent", "b"));
        Assert.Contains("Answer a", results[0].Output.GetProperty("text").GetString());
        Assert.Contains("Answer b", results[1].Output.GetProperty("text").GetString());
        foreach (string model in new[] { "a", "b" })
        {
            Assert.Equal(2, clients.Clients[model].Requests.Count);
            Assert.Contains($"Answer {model}", clients.Clients[model].Requests.Last().Single().Text);
        }
        Assert.All(search.KnowledgeBases, name => Assert.Equal("separate-kb", name));
        Assert.Equal(2, clients.Created.Count);
    }

    [Fact]
    public async Task TranslationTopology_IsModelFreeAndUsesFreshProfileBoundWorkflowExecutors()
    {
        ProfileProvider clients = new();
        foreach (string model in new[] { "a", "b" })
        {
            clients.Clients[model].Reply = (messages, _) => Task.FromResult(messages.Last().Text.Contains("translatedText", StringComparison.OrdinalIgnoreCase)
                ? """{"isValid":true,"confidence":0.95,"issues":[]}"""
                : JsonSerializer.Serialize(new { translatedText = $"Hola {model}" }));
        }
        await using StudioExecutionFixture fixture = Create(clients, new());
        await using var a = fixture.Provider.CreateAsyncScope();
        await using var b = fixture.Provider.CreateAsyncScope();
        await a.ServiceProvider.GetRequiredService<IStudioCatalog>().GetTopologyAsync("translation-workflow", "a", default);
        await b.ServiceProvider.GetRequiredService<IStudioCatalog>().GetTopologyAsync("translation-workflow", "b", default);
        Assert.Empty(clients.Created);
        var sourceA = a.ServiceProvider.GetRequiredService<IAIEntityInspection>().GetWorkflowSource("translation-workflow", new("a"));
        var sourceB = b.ServiceProvider.GetRequiredService<IAIEntityInspection>().GetWorkflowSource("translation-workflow", new("b"));
        Assert.NotSame(sourceA, sourceB);
        Assert.NotSame(sourceA!.CreateWorkflow(), sourceA.CreateWorkflow());
        var result = await Task.WhenAll(Execute(a.ServiceProvider, "a"), Execute(b.ServiceProvider, "b"));
        Assert.Contains("Hola a", result[0].Output.ToString());
        Assert.Contains("Hola b", result[1].Output.ToString());
        Assert.All(result, item => Assert.Equal(StudioOutcomeClassification.Succeeded, item.Classification));
        Assert.All(clients.Clients.Values, client => Assert.Equal(2, client.Requests.Count));
        Assert.Equal(2, clients.Created.Count);

        static Task<StudioEntityExecutionResult> Execute(IServiceProvider services, string profile)
        {
            var request = new StudioRunRequest(profile, "translation-workflow", profile, JsonSerializer.SerializeToElement(new TranslationWorkflowRequest("Hello", ["es"])), null);
            return services.GetRequiredService<IStudioEntityExecutor>().ExecuteAsync(request, new(profile, request.EntityId, profile, null, new("tester")), default).AsTask();
        }
    }

    [Fact]
    public async Task Help_ExecutesHostCapabilityToolAndUsesGroundedValidation()
    {
        ProfileProvider clients = new();
        clients.Clients["a"].Respond = (messages, options, _) =>
        {
            var tool = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().LastOrDefault();
            if (tool is null) return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("help-1", "get_help_capability", new Dictionary<string, object?> { ["capabilityId"] = "run" })])));
            Assert.Contains("run-command", JsonSerializer.Serialize(tool.Result));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"insufficientEvidence":false,"claims":[{"text":"Use `run-command`.","citationIds":["e1"]}]}""")));
        };
        await using StudioExecutionFixture fixture = Create(clients, new());
        var result = await fixture.RunAsync(new HelpAgentRequest("How do I run it?"), "repository-help-agent", "a");
        Assert.Contains("run-command", result.Output.GetProperty("text").GetString());
        Assert.Contains("[Host help]", result.Output.GetProperty("text").GetString());
        Assert.Equal(2, clients.Clients["a"].Requests.Count);
    }

    private static StudioExecutionFixture Create(ProfileProvider clients, SearchFactory search, Action<IServiceCollection>? configure = null) => new(services =>
    {
        services.RemoveAll<IChatClientProvider>();
        services.AddSingleton<IChatClientProvider>(clients);
        services.AddSingleton(new StudioExecutionProfile("a", AIModelSelection.Parse("fake:a")));
        services.AddSingleton(new StudioExecutionProfile("b", AIModelSelection.Parse("fake:b")));
        services.AddSingleton<IKnowledgeSearchFactory>(search);
        services.Configure<BasicRagAgentOptions>(options => options.KnowledgeBase = "separate-kb");
        services.AddSingleton(new HelpAgentDefinition("host-help", "Host help", "Explain this host using its capabilities."));
        services.AddSingleton<IHelpCapabilityCatalog>(new HelpCatalog());
        configure?.Invoke(services);
    });

    private sealed class ProfileProvider : IChatClientProvider
    {
        internal Dictionary<string, StudioExecutionFixture.RecordingClient> Clients { get; } = new() { ["a"] = new(), ["b"] = new() };
        internal ConcurrentQueue<string> Created { get; } = new();
        public string Name => "fake";
        public IChatClient CreateChatClient(string model) { Created.Enqueue(model); return Clients[model]; }
    }
    private sealed class SearchFactory : IKnowledgeSearchFactory, IKnowledgeSearch
    {
        internal ConcurrentQueue<string> KnowledgeBases { get; } = new();
        public IKnowledgeSearch Create(KnowledgeBaseId id, KnowledgeSearchOptions options) { KnowledgeBases.Enqueue(id.Value); return this; }
        public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeSearchResult>>([new("source", "Evidence", "Answer a. Answer b.", null, null, .9)]);
    }
    private sealed class HelpCatalog : IHelpCapabilityCatalog
    {
        public IReadOnlyList<string> CapabilityIds => ["run"];
        public HelpCapability? Find(string id) => id == "run" ? new("run", "Use `run-command`.", "[Host help]", ["run-command"]) : null;
        public IReadOnlyList<HelpCapability> Search(string request, int maxResults = 3) => [Find("run")!];
    }
}
