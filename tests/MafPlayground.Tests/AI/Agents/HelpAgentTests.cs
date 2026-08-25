using System.Runtime.CompilerServices;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Content;
using MafPlayground.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.Tests.AI.Agents;

public sealed class HelpAgentTests
{
    [Fact]
    public void Agent_RejectsMissingFactory()
    {
        Assert.Throws<ArgumentNullException>(() => new HelpAgent(null!));
    }

    [Fact]
    public void Definition_RejectsEmptyHostMetadata()
    {
        Assert.Throws<ArgumentException>(() => new HelpAgentDefinition(
            string.Empty,
            "Description",
            "Instructions"));
    }

    [Fact]
    public void Capability_RejectsEmptyRequiredInlineCode()
    {
        Assert.Throws<ArgumentException>(() => new HelpCapability(
            "reports",
            "Open the reports area.",
            "[Product help]",
            [string.Empty]));
    }

    [Fact]
    public async Task Agent_UsesDedicatedKnowledgeBaseAndGroundedCitations()
    {
        using ToolCallingChatClient chatClient = new(
            "search_knowledge_base",
            "refinedQuery",
            "run the repository help agent",
            """{"insufficientEvidence":false,"claims":[{"text":"Run the repository help agent with agent repository-help.","citationIds":["e1"]}]}""");
        StubKnowledgeSearchFactory searchFactory = new([
            new(
                "cli-reference.md",
                "MafPlayground CLI reference",
                "agent repository-help asks grounded repository questions.",
                null,
                "maf-playground agent repository-help",
                0.91),
        ]);
        HelpAgentOptions options = new()
        {
            KnowledgeBase = "RepositoryHelp",
            Retrieval = new RagRetrievalOptions
            {
                TopK = 6,
                MinimumSimilarity = 0.6,
                MaximumAdditionalSearches = 1,
            },
        };
        HelpAgent agent = new(new HelpAgentFactory(
            chatClient,
            searchFactory,
            new CitationValidator(),
            new UnexpectedRepairService(),
            AgentGuardPipeline.CreateDisabled(),
            new ContentGuard(new RegexPiiContentInspector()),
            new GuardProfileResolver(Options.Create(new AIGuardOptions())),
            CreateDefinition(),
            Options.Create(options),
            new StubCapabilityCatalog()));

        string response = (await agent.Agent.RunAsync(
            "¿Cómo ejecuto el agente de ayuda?")).Text;

        Assert.Equal("RepositoryHelp", searchFactory.KnowledgeBase?.Value);
        Assert.Equal(6, searchFactory.SearchOptions?.TopK);
        Assert.Contains("agent repository-help", response, StringComparison.Ordinal);
        Assert.Contains(
            "[MafPlayground CLI reference, source: cli-reference.md]",
            response,
            StringComparison.Ordinal);
        Assert.Equal(1, searchFactory.SearchCallCount);
        Assert.Contains(
            "Answer in the language used by the user.",
            chatClient.RequestOptions[0]!.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "copy the complete command",
            chatClient.RequestOptions[0]!.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "enumerate every stage",
            chatClient.RequestOptions[0]!.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "inline-code values",
            chatClient.RequestOptions[0]!.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            chatClient.RequestOptions[0]!.Tools!,
            tool => tool.Name == "get_help_capability");
        Assert.Contains(
            chatClient.RequestOptions[0]!.Tools!,
            tool => tool.Name == "find_help_capabilities");
        Assert.Contains("agent repository-help", chatClient.ToolResult?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_ExecutesCliToolAndValidatesItsEvidence()
    {
        using ToolCallingChatClient chatClient = new(
            "get_help_capability",
            "capabilityId",
            "devui",
            """
            {"insufficientEvidence":false,"claims":[{"text":"Run `dotnet run --project src/MafPlayground.CLI -- devui`.","citationIds":["e1"]}]}
            """);
        HelpAgent agent = CreateAgent(
            chatClient,
            new StubCapabilityCatalog());

        string response = (await agent.Agent.RunAsync(
            "¿Cómo ejecuto DevUI?")).Text;

        Assert.Equal(2, chatClient.CallCount);
        Assert.Contains(
            "dotnet run --project src/MafPlayground.CLI -- devui",
            chatClient.ToolResult?.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "[MafPlayground CLI reference, source: cli-reference.md]",
            response,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_CliToolReturnsBoundedErrorForUnknownCommand()
    {
        using ToolCallingChatClient chatClient = new(
            "get_help_capability",
            "capabilityId",
            "unknown command",
            """{"insufficientEvidence":true,"claims":[]}""");
        HelpAgent agent = CreateAgent(
            chatClient,
            new StubCapabilityCatalog());

        string response = (await agent.Agent.RunAsync(
            "Run a command that does not exist.")).Text;

        Assert.Equal(2, chatClient.CallCount);
        Assert.Contains(
            "Unknown capability ID",
            chatClient.ToolResult?.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, response);
    }

    [Fact]
    public async Task Agent_GetHelpCapabilityToolValidatesCapabilityId()
    {
        using ToolCallingChatClient chatClient = new(
            "get_help_capability",
            "capabilityId",
            new string('x', 513),
            """{"insufficientEvidence":true,"claims":[]}""");
        HelpAgent agent = CreateAgent(
            chatClient,
            new StubCapabilityCatalog());

        string response = (await agent.Agent.RunAsync(
            "Find an invalid capability.")).Text;

        Assert.Contains(
            "between 1 and 512 characters",
            chatClient.ToolResult?.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, response);
    }

    [Fact]
    public async Task Agent_DoesNotRetrieveContextForPurelySocialTurn()
    {
        using FakeChatClient chatClient = new(
            """
            {"insufficientEvidence":false,"claims":[],"conversationalResponse":"¡Hola, Lautaro! Cuando quieras, haceme tus consultas."}
            """);
        StubKnowledgeSearchFactory searchFactory = new([]);
        StubCapabilityCatalog capabilityCatalog = new();
        HelpAgent agent = CreateAgent(chatClient, capabilityCatalog, searchFactory: searchFactory);

        string response = (await agent.Agent.RunAsync(
            "Hola, mi nombre es Lautaro y voy a hacerte un par de consultas.")).Text;

        Assert.Equal("¡Hola, Lautaro! Cuando quieras, haceme tus consultas.", response);
        Assert.Equal(0, searchFactory.SearchCallCount);
        Assert.Equal(0, capabilityCatalog.SearchCallCount);
        Assert.Equal(0, capabilityCatalog.FindCallCount);
    }

    [Fact]
    public async Task Agent_PreservesSocialIntroductionInConversationSession()
    {
        const string greeting =
            "¡Hola, Lautaro! Cuando quieras, haceme tus consultas.";
        using FakeChatClient chatClient = new(
            $$"""
            {"insufficientEvidence":false,"claims":[],"conversationalResponse":"{{greeting}}"}
            """);
        chatClient.EnqueueResponse(
            """{"insufficientEvidence":true,"claims":[],"clarifyingQuestion":"¿Sobre qué parte de la app querés consultar?"}""");
        HelpAgent agent = CreateAgent(chatClient, new StubCapabilityCatalog());
        Microsoft.Agents.AI.AgentSession session =
            await agent.Agent.CreateSessionAsync();

        await agent.Agent.RunAsync(
            "Hola, mi nombre es Lautaro y voy a hacerte un par de consultas.",
            session);
        await agent.Agent.RunAsync("Tengo una duda.", session);

        Assert.Contains(
            chatClient.Requests[1],
            message => message.Role == ChatRole.User &&
                message.Text?.Contains("Lautaro", StringComparison.Ordinal) == true);
        Assert.Contains(
            chatClient.Requests[1],
            message => message.Role == ChatRole.Assistant &&
                message.Text == greeting);
    }

    [Fact]
    public async Task Agent_RepairsCliAnswerThatOmitsRequiredInlineInvocation()
    {
        const string invocation =
            "dotnet run --project src/MafPlayground.CLI -- devui";
        using ToolCallingChatClient chatClient = new(
            "get_help_capability",
            "capabilityId",
            "devui",
            $$"""
            {"insufficientEvidence":false,"claims":[{"text":"Run '{{invocation}}'.","citationIds":["e1"]}]}
            """);
        RecordingRepairService repairService = new(
            new RagAnswerDraft(
                false,
                [new RagClaim($"Run `{invocation}`.", ["e1"])]));
        HelpAgent agent = CreateAgent(
            chatClient,
            new StubCapabilityCatalog(),
            repairService);

        string response = (await agent.Agent.RunAsync(
            "How do I run DevUI?")).Text;

        Assert.True(repairService.WasCalled);
        Assert.Contains($"`{invocation}`", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_ReturnsSafeFallbackWhenCliRepairRemainsInvalid()
    {
        using ToolCallingChatClient chatClient = new(
            "get_help_capability",
            "capabilityId",
            "devui",
            """
            {"insufficientEvidence":true,"claims":[]}
            """);
        RecordingRepairService repairService = new(
            new RagAnswerDraft(true, []));
        HelpAgent agent = CreateAgent(
            chatClient,
            new StubCapabilityCatalog(),
            repairService);

        string response = (await agent.Agent.RunAsync(
            "How do I run DevUI?")).Text;

        Assert.True(repairService.WasCalled);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, response);
    }

    [Fact]
    public async Task Agent_FindHelpCapabilitiesToolResolvesNaturalLanguageRequest()
    {
        using ToolCallingChatClient chatClient = new(
            "find_help_capabilities",
            "request",
            "run devui",
            """
            {"insufficientEvidence":false,"claims":[{"text":"Run `dotnet run --project src/MafPlayground.CLI -- devui`.","citationIds":["e1"]}]}
            """);
        HelpAgent agent = CreateAgent(
            chatClient,
            new StubCapabilityCatalog());

        string response = (await agent.Agent.RunAsync(
            "Which CLI command should I use?")).Text;

        Assert.Equal(2, chatClient.CallCount);
        Assert.Contains(
            "dotnet run --project src/MafPlayground.CLI -- devui",
            chatClient.ToolResult?.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "[MafPlayground CLI reference, source: cli-reference.md]",
            response,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_FindHelpCapabilitiesToolReturnsBoundedNoMatchResult()
    {
        using ToolCallingChatClient chatClient = new(
            "find_help_capabilities",
            "request",
            "operate the warp drive",
            """{"insufficientEvidence":true,"claims":[]}""");
        HelpAgent agent = CreateAgent(
            chatClient,
            new StubCapabilityCatalog());

        string response = (await agent.Agent.RunAsync(
            "Which CLI command should I use?")).Text;

        Assert.Contains(
            "No matching application capability was found",
            chatClient.ToolResult?.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, response);
    }

    [Fact]
    public async Task Agent_PreservesClarifyingQuestionInConversationSession()
    {
        const string clarifyingQuestion =
            "Which part of the application do you want help with?";
        using FakeChatClient chatClient = new(
            $$"""
            {"insufficientEvidence":true,"claims":[],"clarifyingQuestion":"{{clarifyingQuestion}}"}
            """);
        chatClient.EnqueueResponse(
            """{"insufficientEvidence":true,"claims":[],"clarifyingQuestion":null}""");
        HelpAgent agent = CreateAgent(chatClient, new StubCapabilityCatalog());
        Microsoft.Agents.AI.AgentSession session =
            await agent.Agent.CreateSessionAsync();

        string firstResponse = (await agent.Agent.RunAsync(
            "Help me.",
            session)).Text;
        string secondResponse = (await agent.Agent.RunAsync(
            "The reporting area.",
            session)).Text;

        Assert.Equal(clarifyingQuestion, firstResponse);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, secondResponse);
        Assert.Contains(
            chatClient.Requests[1],
            message => message.Role == ChatRole.Assistant &&
                message.Text == clarifyingQuestion);
    }

    [Fact]
    public async Task Agent_FindHelpCapabilitiesToolReturnsBoundedValidationResult()
    {
        using ToolCallingChatClient chatClient = new(
            "find_help_capabilities",
            "request",
            new string('x', 513),
            """{"insufficientEvidence":true,"claims":[]}""");
        HelpAgent agent = CreateAgent(
            chatClient,
            new StubCapabilityCatalog());

        string response = (await agent.Agent.RunAsync(
            "Which CLI command should I use?")).Text;

        Assert.Contains(
            "between 1 and 512 characters",
            chatClient.ToolResult?.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, response);
    }

    private static HelpAgent CreateAgent(
        IChatClient chatClient,
        IHelpCapabilityCatalog capabilityCatalog,
        IRagAnswerRepairService? repairService = null,
        StubKnowledgeSearchFactory? searchFactory = null) =>
        new(new HelpAgentFactory(
            chatClient,
            searchFactory ?? new StubKnowledgeSearchFactory([]),
            new CitationValidator(),
            repairService ?? new UnexpectedRepairService(),
            AgentGuardPipeline.CreateDisabled(),
            new ContentGuard(new RegexPiiContentInspector()),
            new GuardProfileResolver(Options.Create(new AIGuardOptions())),
            CreateDefinition(),
            Options.Create(new HelpAgentOptions { KnowledgeBase = "RepositoryHelp" }),
            capabilityCatalog));

    private static HelpAgentDefinition CreateDefinition() => new(
        "test-help-agent",
        "Test help agent.",
        "Explain how to use the repository and copy the complete command. When explaining a pipeline, enumerate every stage.");

    private sealed class RecordingRepairService(RagAnswerDraft repairedDraft)
        : IRagAnswerRepairService
    {
        public bool WasCalled { get; private set; }

        public Task<RagAnswerDraft> RepairAsync(
            string question,
            IReadOnlyCollection<RagEvidence> frozenEvidence,
            RagAnswerDraft invalidDraft,
            IReadOnlyList<string> validationIssues,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(repairedDraft);
        }
    }

    private sealed class StubKnowledgeSearchFactory(
        IReadOnlyList<KnowledgeSearchResult> results) : IKnowledgeSearchFactory
    {
        public KnowledgeBaseId? KnowledgeBase { get; private set; }

        public KnowledgeSearchOptions? SearchOptions { get; private set; }

        public int SearchCallCount { get; private set; }

        public IKnowledgeSearch Create(
            KnowledgeBaseId knowledgeBaseId,
            KnowledgeSearchOptions searchOptions)
        {
            KnowledgeBase = knowledgeBaseId;
            SearchOptions = searchOptions;
            return new StubKnowledgeSearch(results, () => SearchCallCount++);
        }
    }

    private sealed class StubKnowledgeSearch(
        IReadOnlyList<KnowledgeSearchResult> results,
        Action onSearch) : IKnowledgeSearch
    {
        public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            onSearch();
            return Task.FromResult(results);
        }
    }

    private sealed class UnexpectedRepairService : IRagAnswerRepairService
    {
        public Task<RagAnswerDraft> RepairAsync(
            string question,
            IReadOnlyCollection<RagEvidence> frozenEvidence,
            RagAnswerDraft invalidDraft,
            IReadOnlyList<string> validationIssues,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Repair was not expected.");
    }

    private sealed class StubCapabilityCatalog : IHelpCapabilityCatalog
    {
        public int FindCallCount { get; private set; }

        public int SearchCallCount { get; private set; }

        public IReadOnlyList<string> CapabilityIds =>
            ["agent repository-help", "devui"];

        public HelpCapability? Find(string capabilityId)
        {
            FindCallCount++;
            return capabilityId switch
            {
                "agent repository-help" => CreateCapability(
                    "agent repository-help",
                    "dotnet run --project src/MafPlayground.CLI -- agent repository-help",
                    "Run repository help."),
                "devui" => CreateCapability(
                        "devui",
                        "dotnet run --project src/MafPlayground.CLI -- devui",
                        "Run DevUI."),
                _ => null,
            };
        }

        public IReadOnlyList<HelpCapability> Search(
            string request,
            int maxResults = 3)
        {
            SearchCallCount++;
            if (request.Contains("devui", StringComparison.OrdinalIgnoreCase))
            {
                return [Find("devui")!];
            }
            if (request.Contains(
                    "repository-help",
                    StringComparison.OrdinalIgnoreCase))
            {
                return [Find("agent repository-help")!];
            }
            return [];
        }

        private static HelpCapability CreateCapability(
            string id,
            string invocation,
            string description) => new(
                id,
                $"Command `{id}` uses the exact invocation `{invocation}`. {description}",
                "[MafPlayground CLI reference, source: cli-reference.md]",
                [invocation]);
    }

    private sealed class ToolCallingChatClient(
        string toolName,
        string argumentName,
        string argumentValue,
        string finalResponse) : IChatClient
    {
        private int _callCount;

        public int CallCount => _callCount;

        public object? ToolResult { get; private set; }

        public List<ChatOptions?> RequestOptions { get; } = [];

        public ChatClientMetadata Metadata { get; } = new("fake");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestOptions.Add(options);
            ChatMessage[] request = messages.ToArray();
            ToolResult = request
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
                .LastOrDefault()?.Result ?? ToolResult;
            ChatMessage response = Interlocked.Increment(ref _callCount) == 1
                ? new ChatMessage(
                    ChatRole.Assistant,
                    [
                        new FunctionCallContent(
                            "cli-call",
                            toolName,
                            new Dictionary<string, object?>
                            {
                                [argumentName] = argumentValue,
                            }),
                    ])
                : new ChatMessage(ChatRole.Assistant, finalResponse);
            return Task.FromResult(new ChatResponse(response));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
