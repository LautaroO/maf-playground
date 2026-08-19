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
    }

    [Fact]
    public async Task Agent_ExecutesCliToolAndValidatesItsEvidence()
    {
        using ToolCallingChatClient chatClient = new(
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

    private static RepositoryHelpAgent CreateAgent(
        IChatClient chatClient,
        IRepositoryCliCommandCatalog commandCatalog) =>
        new(
            chatClient,
            new StubKnowledgeSearchFactory([]),
            new CitationValidator(),
            new UnexpectedRepairService(),
            AgentGuardPipeline.CreateDisabled(),
            new ContentGuard(new RegexPiiContentInspector()),
            new GuardProfileResolver(Options.Create(new AIGuardOptions())),
            Options.Create(new RepositoryHelpAgentOptions()),
            commandCatalog);

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
        public IReadOnlyList<string> CommandPaths => ["devui"];

        public RepositoryCliCommand? Find(string commandPath) =>
            commandPath == "devui"
                ? new RepositoryCliCommand(
                    "devui",
                    "dotnet run --project src/MafPlayground.CLI -- devui",
                    "Run DevUI.")
                : null;
    }

    private sealed class ToolCallingChatClient(
        string commandPath,
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
                            "get_cli_command",
                            new Dictionary<string, object?>
                            {
                                ["commandPath"] = commandPath,
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
