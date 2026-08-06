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
        IRagAnswerRepairService repairService,
        AgentGuardPipeline guardPipeline,
        IOptions<BasicRagAgentOptions> options,
        IOptions<AgentTelemetryOptions>? telemetryOptions = null)
    {
        ChatClientAgent chatAgent = new(chatClient, new ChatClientAgentOptions
        {
            Name = "basic-rag-agent",
            Description = "A grounded help assistant that answers from an ingested document knowledge base with citations.",
            ChatOptions = new ChatOptions
            {
                Instructions = "Answer clearly and concisely. Use only retrieved knowledge-base evidence and preserve exact factual values.",
            },
            AIContextProviders = [contextProvider],
        });

        AIAgent structuredAgent = new StructuredRagAgent(
            chatAgent,
            invocationContextAccessor,
            citationValidator,
            repairService);
        AIAgent guardedAgent = guardPipeline.Apply(
            structuredAgent,
            options.Value.GuardProfile);

        bool sensitiveData = telemetryOptions?.Value.EnableSensitiveData ?? false;
        Agent = guardedAgent.AsBuilder()
            .UseOpenTelemetry(AITelemetry.AgentSourceName, telemetry => telemetry.EnableSensitiveData = sensitiveData)
            .Build();
    }

    public AIAgent Agent { get; }
}
