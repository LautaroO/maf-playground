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
            "A password-reset link remains valid for 30 minutes " +
            "[help, page 2, source: help.pdf].");
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
            "A password-reset link remains valid for 30 minutes " +
            "[help, page 2, source: help.pdf].");
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
            "A password-reset link remains valid for 30 minutes.");
        chatClient.EnqueueResponse(
            "A password-reset link remains valid for 30 minutes " +
            "[help, page 2, source: help.pdf].");
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
            MafPlayground.AI.Guards.AgentGuardPipeline.CreateDisabled(),
            Microsoft.Extensions.Options.Options.Create(new BasicRagAgentOptions()));
        AgentSession session = await agent.Agent.CreateSessionAsync();

        string response = (await agent.Agent.RunAsync(
            "How long does a password-reset link remain valid?",
            session)).Text;

        Assert.Equal(2, chatClient.Requests.Count);
        Assert.Contains("30 minutes", response);
        Assert.Contains("[help, page 2, source: help.pdf]", response);
    }

    private sealed class StubKnowledgeSearch(IReadOnlyList<KnowledgeSearchResult> results)
        : IKnowledgeSearch
    {
        public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) => Task.FromResult(
                string.IsNullOrWhiteSpace(query) ? [] : results);
    }
}
