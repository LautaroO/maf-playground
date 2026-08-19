using MafPlayground.AI.Guards;
using MafPlayground.AI.Observability;
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
        Agent = GroundedKnowledgeAgentComposer.Create(
            chatClient,
            "basic-rag-agent",
            "A grounded help assistant that answers from an ingested document knowledge base with citations.",
            "Answer clearly and concisely. Use only retrieved knowledge-base evidence and preserve exact factual values.",
            [contextProvider],
            invocationContextAccessor,
            citationValidator,
            repairService,
            guardPipeline,
            options.Value.GuardProfile,
            telemetryOptions?.Value.EnableSensitiveData ?? false);
    }

    public AIAgent Agent { get; }
}
