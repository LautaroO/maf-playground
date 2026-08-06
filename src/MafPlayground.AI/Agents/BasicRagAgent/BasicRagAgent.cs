using MafPlayground.AI.Guards;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Agents.BasicRagAgent;

public sealed class BasicRagAgent
{
    public BasicRagAgent(
        IChatClient chatClient,
        RagContextProvider contextProvider,
        RagInvocationContextAccessor invocationContextAccessor,
        CitationValidator citationValidator,
        AgentGuardPipeline guardPipeline,
        IOptions<BasicRagAgentOptions> options,
        IOptions<AgentTelemetryOptions>? telemetryOptions = null)
    {
        AIAgent inner = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "basic-rag-agent",
            Description = "A grounded help assistant that answers from an ingested document knowledge base with citations.",
            ChatOptions = new ChatOptions
            {
                Instructions = "Answer clearly and concisely. Use only retrieved knowledge-base evidence and preserve exact factual values.",
            },
            AIContextProviders = [contextProvider],
        });

        AIAgentBuilder builder = inner.AsBuilder()
            .Use(async (messages, session, runOptions, agent, cancellationToken) =>
            {
                using RagInvocationScope invocationScope =
                    invocationContextAccessor.BeginScope();
                AgentResponse response = await agent.RunAsync(messages, session, runOptions, cancellationToken);
                RagInvocationContext invocationContext = invocationScope.Context;
                if (citationValidator.IsValid(
                        response.Text,
                        invocationContext.AllowedCitations))
                {
                    return response;
                }

                if (invocationContext.AllowedCitations.Count > 0)
                {
                    HashSet<string> repairCitations = new(
                        invocationContext.AllowedCitations,
                        StringComparer.Ordinal);
                    List<ChatMessage> repairMessages = messages.ToList();
                    repairMessages.Add(new ChatMessage(
                        ChatRole.System,
                        BuildCitationRepairInstruction(repairCitations)));
                    AgentResponse repairedResponse = await agent.RunAsync(
                        repairMessages,
                        session,
                        runOptions,
                        cancellationToken);
                    if (citationValidator.IsValid(
                            repairedResponse.Text,
                            repairCitations))
                    {
                        return repairedResponse;
                    }
                }

                return new AgentResponse(new ChatMessage(
                    ChatRole.Assistant,
                    CitationValidator.NoEvidenceAnswer));
            }, null);
        AIAgent guardedAgent = guardPipeline.Apply(
            builder.Build(),
            options.Value.GuardProfile);

        bool sensitiveData = telemetryOptions?.Value.EnableSensitiveData ?? false;
        Agent = guardedAgent.AsBuilder()
            .UseOpenTelemetry(AITelemetry.AgentSourceName, telemetry => telemetry.EnableSensitiveData = sensitiveData)
            .Build();
    }

    public AIAgent Agent { get; }

    private static string BuildCitationRepairInstruction(IEnumerable<string> citations) =>
        $"""
        The previous draft was rejected because it did not use a valid citation.
        Rewrite the answer without adding unsupported facts. Include one or more of the following citations exactly as written:
        {string.Join(Environment.NewLine, citations)}
        """;
}
