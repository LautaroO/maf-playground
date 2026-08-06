using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.Retrieval;
using Microsoft.Agents.AI;

namespace MafPlayground.Tests.AI.Agents.BasicRagAgent;

public sealed class BasicRagAgentTests
{
    [Fact]
    public async Task Agent_PreservesGroundedResponseWithRetrievedCitation()
    {
        using FakeChatClient chatClient = new(
            """{"insufficientEvidence":false,"claims":[{"text":"A password-reset link remains valid for 30 minutes.","citationIds":["e1"]}]}""");
        StubKnowledgeSearch search = new([
            new("help.pdf", "help", "The reset link expires 30 minutes after it is issued.", 2, "Page 2", 0.716),
        ]);
        RagInvocationContextAccessor invocationContextAccessor = new();
        RagContextProvider contextProvider = new(
            search,
            new RagRetrievalOptions { MaximumAdditionalSearches = 1 },
            invocationContextAccessor);
        MafPlayground.AI.Agents.BasicRagAgent.BasicRagAgent agent = new(
            chatClient,
            contextProvider,
            invocationContextAccessor,
            new CitationValidator(),
            new StubRepairService(),
            MafPlayground.AI.Guards.AgentGuardPipeline.CreateDisabled(),
            Microsoft.Extensions.Options.Options.Create(new BasicRagAgentOptions()));

        AgentSession session = await agent.Agent.CreateSessionAsync();
        string response = (await agent.Agent.RunAsync(
            "How long does a password-reset link remain valid?",
            session)).Text;

        Assert.Contains("30 minutes", response);
        Assert.DoesNotContain(
            "The reset link expires",
            Assert.Single(chatClient.RequestOptions)!.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            Assert.Single(chatClient.Requests),
            message => message.Role == Microsoft.Extensions.AI.ChatRole.User &&
                message.Text.Contains(
                    "<knowledge_base_evidence>",
                    StringComparison.Ordinal) &&
                message.Text.Contains(
                    "The reset link expires",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task Agent_StreamingPreservesGroundedResponseWithRetrievedCitation()
    {
        using FakeChatClient chatClient = new(
            """{"insufficientEvidence":false,"claims":[{"text":"A password-reset link remains valid for 30 minutes.","citationIds":["e1"]}]}""");
        StubKnowledgeSearch search = new([
            new("help.pdf", "help", "The reset link expires 30 minutes after it is issued.", 2, "Page 2", 0.716),
        ]);
        RagInvocationContextAccessor invocationContextAccessor = new();
        RagContextProvider contextProvider = new(
            search,
            new RagRetrievalOptions { MaximumAdditionalSearches = 1 },
            invocationContextAccessor);
        MafPlayground.AI.Agents.BasicRagAgent.BasicRagAgent agent = new(
            chatClient,
            contextProvider,
            invocationContextAccessor,
            new CitationValidator(),
            new StubRepairService(),
            MafPlayground.AI.Guards.AgentGuardPipeline.CreateDisabled(),
            Microsoft.Extensions.Options.Options.Create(new BasicRagAgentOptions()));
        AgentSession session = await agent.Agent.CreateSessionAsync();
        List<string> updates = [];

        await foreach (AgentResponseUpdate update in agent.Agent.RunStreamingAsync(
                           "How long does a password-reset link remain valid?",
                           session))
        {
            updates.Add(update.Text);
        }

        Assert.Contains("30 minutes", string.Concat(updates));
    }

    [Fact]
    public async Task Agent_RepairsGroundedResponseThatOmitsCitationOnce()
    {
        using FakeChatClient chatClient = new(
            """{"insufficientEvidence":false,"claims":[{"text":"A password-reset link remains valid for 30 minutes.","citationIds":["invented"]}]}""");
        StubRepairService repairService = new(new RagAnswerDraft(
            false,
            [new RagClaim(
                "A password-reset link remains valid for 30 minutes.",
                ["e1"])]));
        StubKnowledgeSearch search = new([
            new("help.pdf", "help", "The reset link expires 30 minutes after it is issued.", 2, "Page 2", 0.716),
        ]);
        RagInvocationContextAccessor invocationContextAccessor = new();
        RagContextProvider contextProvider = new(
            search,
            new RagRetrievalOptions { MaximumAdditionalSearches = 1 },
            invocationContextAccessor);
        MafPlayground.AI.Agents.BasicRagAgent.BasicRagAgent agent = new(
            chatClient,
            contextProvider,
            invocationContextAccessor,
            new CitationValidator(),
            repairService,
            MafPlayground.AI.Guards.AgentGuardPipeline.CreateDisabled(),
            Microsoft.Extensions.Options.Options.Create(new BasicRagAgentOptions()));
        AgentSession session = await agent.Agent.CreateSessionAsync();

        string response = (await agent.Agent.RunAsync(
            "How long does a password-reset link remain valid?",
            session)).Text;

        Assert.Single(chatClient.Requests);
        Assert.Equal(1, repairService.Calls);
        Assert.Contains("30 minutes", response);
        Assert.Contains("[help, page 2, source: help.pdf]", response);
        Assert.NotNull(repairService.FrozenEvidence);
        Assert.Single(repairService.FrozenEvidence!);
        Assert.DoesNotContain("invented", response, StringComparison.Ordinal);
        Assert.True(session.TryGetInMemoryChatHistory(out List<Microsoft.Extensions.AI.ChatMessage>? history));
        Assert.DoesNotContain(
            history,
            message => message.Text.Contains("invented", StringComparison.Ordinal));
        Assert.Contains(
            history,
            message => message.Role == Microsoft.Extensions.AI.ChatRole.Assistant &&
                message.Text.Contains("[help, page 2, source: help.pdf]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Agent_NoEvidenceRejectsModelClaimsAndReturnsExactFallback()
    {
        using FakeChatClient chatClient = new(
            """{"insufficientEvidence":false,"claims":[{"text":"Invented answer after the fallback.","citationIds":[]}]}""");
        RagInvocationContextAccessor invocationContextAccessor = new();
        RagContextProvider contextProvider = new(
            new StubKnowledgeSearch([]),
            new RagRetrievalOptions { MaximumAdditionalSearches = 0 },
            invocationContextAccessor);
        StubRepairService repairService = new();
        MafPlayground.AI.Agents.BasicRagAgent.BasicRagAgent agent = new(
            chatClient,
            contextProvider,
            invocationContextAccessor,
            new CitationValidator(),
            repairService,
            MafPlayground.AI.Guards.AgentGuardPipeline.CreateDisabled(),
            Microsoft.Extensions.Options.Options.Create(new BasicRagAgentOptions()));

        string response = (await agent.Agent.RunAsync(
            "What is the secret launch code?")).Text;

        Assert.Equal(CitationValidator.NoEvidenceAnswer, response);
        Assert.Equal(0, repairService.Calls);
    }

    private sealed class StubKnowledgeSearch(IReadOnlyList<KnowledgeSearchResult> results)
        : IKnowledgeSearch
    {
        public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) => Task.FromResult(
                string.IsNullOrWhiteSpace(query) ? [] : results);
    }

    private sealed class StubRepairService(RagAnswerDraft? repairedDraft = null)
        : IRagAnswerRepairService
    {
        public int Calls { get; private set; }

        public IReadOnlyCollection<RagEvidence>? FrozenEvidence { get; private set; }

        public Task<RagAnswerDraft> RepairAsync(
            string question,
            IReadOnlyCollection<RagEvidence> frozenEvidence,
            RagAnswerDraft invalidDraft,
            IReadOnlyList<string> validationIssues,
            CancellationToken cancellationToken)
        {
            Calls++;
            FrozenEvidence = frozenEvidence;
            return Task.FromResult(repairedDraft ?? throw new InvalidOperationException(
                "Repair was not expected."));
        }
    }
}
