using System.Runtime.CompilerServices;
using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Agents.RepositoryHelpAgent;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Guards.Content;
using MafPlayground.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.Tests.AI.Agents;

public sealed class RepositoryHelpAgentTests
{
    [Fact]
    public async Task Agent_UsesDedicatedKnowledgeBaseAndGroundedCitations()
    {
        using FakeChatClient chatClient = new(
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
        RepositoryHelpAgentOptions options = new()
        {
            KnowledgeBase = "RepositoryHelp",
            Retrieval = new RagRetrievalOptions
            {
                TopK = 6,
                MinimumSimilarity = 0.6,
                MaximumAdditionalSearches = 1,
            },
        };
        RepositoryHelpAgent agent = new(
            chatClient,
            searchFactory,
            new CitationValidator(),
            new UnexpectedRepairService(),
            AgentGuardPipeline.CreateDisabled(),
            new ContentGuard(new RegexPiiContentInspector()),
            new GuardProfileResolver(Options.Create(new AIGuardOptions())),
            Options.Create(options),
            new StubCommandCatalog());

        string response = (await agent.Agent.RunAsync(
            "¿Cómo ejecuto el agente de ayuda?")).Text;

        Assert.Equal("RepositoryHelp", searchFactory.KnowledgeBase?.Value);
        Assert.Equal(6, searchFactory.SearchOptions?.TopK);
        Assert.Contains("agent repository-help", response, StringComparison.Ordinal);
        Assert.Contains(
            "[MafPlayground CLI reference, source: cli-reference.md]",
            response,
            StringComparison.Ordinal);
        Assert.Contains(
            "Answer in the language used by the user.",
            Assert.Single(chatClient.RequestOptions)!.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "copy the complete command",
            Assert.Single(chatClient.RequestOptions)!.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "enumerate every stage",
            Assert.Single(chatClient.RequestOptions)!.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "inline backticks",
            Assert.Single(chatClient.RequestOptions)!.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            Assert.Single(chatClient.RequestOptions)!.Tools!,
            tool => tool.Name == "get_cli_command");
        Assert.Contains(
            Assert.Single(chatClient.RequestOptions)!.Tools!,
            tool => tool.Name == "find_cli_commands");
        Assert.Contains(
            Assert.Single(chatClient.Requests),
            message => message.Text?.Contains(
                "dotnet run --project src/MafPlayground.CLI -- agent repository-help",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Agent_ExecutesCliToolAndValidatesItsEvidence()
    {
        using ToolCallingChatClient chatClient = new(
            "get_cli_command",
            "commandPath",
            "devui",
            """
            {"insufficientEvidence":false,"claims":[{"text":"Run `dotnet run --project src/MafPlayground.CLI -- devui`.","citationIds":["e1"]}]}
            """);
        RepositoryHelpAgent agent = CreateAgent(
            chatClient,
            new StubCommandCatalog());

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
            "get_cli_command",
            "commandPath",
            "unknown command",
            """{"insufficientEvidence":true,"claims":[]}""");
        RepositoryHelpAgent agent = CreateAgent(
            chatClient,
            new StubCommandCatalog());

        string response = (await agent.Agent.RunAsync(
            "Run a command that does not exist.")).Text;

        Assert.Equal(2, chatClient.CallCount);
        Assert.Contains(
            "Unknown command path",
            chatClient.ToolResult?.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, response);
    }

    [Fact]
    public async Task Agent_AutomaticallySuppliesExactLiveCommandEvidence()
    {
        using FakeChatClient chatClient = new(
            """
            {"insufficientEvidence":false,"claims":[{"text":"Run `dotnet run --project src/MafPlayground.CLI -- devui`.","citationIds":["e1"]}]}
            """);
        RepositoryHelpAgent agent = CreateAgent(
            chatClient,
            new StubCommandCatalog());

        string response = (await agent.Agent.RunAsync(
            "¿Cómo ejecuto DevUI?")).Text;

        IReadOnlyList<ChatMessage> request = Assert.Single(chatClient.Requests);
        Assert.Contains(
            request,
            message => message.Text?.Contains(
                "matching live CLI commands",
                StringComparison.Ordinal) == true);
        Assert.Contains(
            "dotnet run --project src/MafPlayground.CLI -- devui",
            response,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_RepairsCliAnswerThatOmitsRequiredInlineInvocation()
    {
        const string invocation =
            "dotnet run --project src/MafPlayground.CLI -- devui";
        using FakeChatClient chatClient = new(
            $$"""
            {"insufficientEvidence":false,"claims":[{"text":"Run '{{invocation}}'.","citationIds":["e1"]}]}
            """);
        RecordingRepairService repairService = new(
            new RagAnswerDraft(
                false,
                [new RagClaim($"Run `{invocation}`.", ["e1"])]));
        RepositoryHelpAgent agent = CreateAgent(
            chatClient,
            new StubCommandCatalog(),
            repairService);

        string response = (await agent.Agent.RunAsync(
            "How do I run DevUI?")).Text;

        Assert.True(repairService.WasCalled);
        Assert.Contains($"`{invocation}`", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_ReturnsSafeFallbackWhenCliRepairRemainsInvalid()
    {
        using FakeChatClient chatClient = new(
            """
            {"insufficientEvidence":true,"claims":[]}
            """);
        RecordingRepairService repairService = new(
            new RagAnswerDraft(true, []));
        RepositoryHelpAgent agent = CreateAgent(
            chatClient,
            new StubCommandCatalog(),
            repairService);

        string response = (await agent.Agent.RunAsync(
            "How do I run DevUI?")).Text;

        Assert.True(repairService.WasCalled);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, response);
    }

    [Fact]
    public async Task Agent_FindCliCommandsToolResolvesNaturalLanguageRequest()
    {
        using ToolCallingChatClient chatClient = new(
            "find_cli_commands",
            "request",
            "run devui",
            """
            {"insufficientEvidence":false,"claims":[{"text":"Run `dotnet run --project src/MafPlayground.CLI -- devui`.","citationIds":["e1"]}]}
            """);
        RepositoryHelpAgent agent = CreateAgent(
            chatClient,
            new StubCommandCatalog());

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
    public async Task Agent_FindCliCommandsToolReturnsBoundedNoMatchResult()
    {
        using ToolCallingChatClient chatClient = new(
            "find_cli_commands",
            "request",
            "operate the warp drive",
            """{"insufficientEvidence":true,"claims":[]}""");
        RepositoryHelpAgent agent = CreateAgent(
            chatClient,
            new StubCommandCatalog());

        string response = (await agent.Agent.RunAsync(
            "Which CLI command should I use?")).Text;

        Assert.Contains(
            "No matching CLI command was found",
            chatClient.ToolResult?.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, response);
    }

    [Fact]
    public async Task Agent_FindCliCommandsToolReturnsBoundedValidationResult()
    {
        using ToolCallingChatClient chatClient = new(
            "find_cli_commands",
            "request",
            new string('x', 513),
            """{"insufficientEvidence":true,"claims":[]}""");
        RepositoryHelpAgent agent = CreateAgent(
            chatClient,
            new StubCommandCatalog());

        string response = (await agent.Agent.RunAsync(
            "Which CLI command should I use?")).Text;

        Assert.Contains(
            "between 1 and 512 characters",
            chatClient.ToolResult?.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(CitationValidator.NoEvidenceAnswer, response);
    }

    private static RepositoryHelpAgent CreateAgent(
        IChatClient chatClient,
        IRepositoryCliCommandCatalog commandCatalog,
        IRagAnswerRepairService? repairService = null) =>
        new(
            chatClient,
            new StubKnowledgeSearchFactory([]),
            new CitationValidator(),
            repairService ?? new UnexpectedRepairService(),
            AgentGuardPipeline.CreateDisabled(),
            new ContentGuard(new RegexPiiContentInspector()),
            new GuardProfileResolver(Options.Create(new AIGuardOptions())),
            Options.Create(new RepositoryHelpAgentOptions()),
            commandCatalog);

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

        public IKnowledgeSearch Create(
            KnowledgeBaseId knowledgeBaseId,
            KnowledgeSearchOptions searchOptions)
        {
            KnowledgeBase = knowledgeBaseId;
            SearchOptions = searchOptions;
            return new StubKnowledgeSearch(results);
        }
    }

    private sealed class StubKnowledgeSearch(
        IReadOnlyList<KnowledgeSearchResult> results) : IKnowledgeSearch
    {
        public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(results);
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

    private sealed class StubCommandCatalog : IRepositoryCliCommandCatalog
    {
        public IReadOnlyList<string> CommandPaths =>
            ["agent repository-help", "devui"];

        public RepositoryCliCommand? Find(string commandPath) => commandPath switch
        {
            "agent repository-help" => new RepositoryCliCommand(
                "agent repository-help",
                "dotnet run --project src/MafPlayground.CLI -- agent repository-help",
                "Run repository help."),
            "devui" => new RepositoryCliCommand(
                    "devui",
                    "dotnet run --project src/MafPlayground.CLI -- devui",
                    "Run DevUI."),
            _ => null,
        };

        public IReadOnlyList<RepositoryCliCommand> Search(
            string request,
            int maxResults = 3)
        {
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

        public ChatClientMetadata Metadata { get; } = new("fake");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
