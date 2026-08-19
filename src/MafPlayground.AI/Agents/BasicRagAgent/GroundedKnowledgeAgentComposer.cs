using MafPlayground.AI.Guards;
using MafPlayground.AI.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Agents.BasicRagAgent;

internal static class GroundedKnowledgeAgentComposer
{
    public static AIAgent Create(
        IChatClient chatClient,
        string name,
        string description,
        string instructions,
        IEnumerable<AIContextProvider> contextProviders,
        RagInvocationContextAccessor invocationContextAccessor,
        CitationValidator citationValidator,
        IRagAnswerRepairService repairService,
        AgentGuardPipeline guardPipeline,
        string guardProfile,
        bool enableSensitiveTelemetry)
    {
        ChatClientAgent chatAgent = new(chatClient, new ChatClientAgentOptions
        {
            Name = name,
            Description = description,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
            },
            AIContextProviders = contextProviders.ToArray(),
        });

        AIAgent structuredAgent = new StructuredRagAgent(
            chatAgent,
            invocationContextAccessor,
            citationValidator,
            repairService);
        AIAgent guardedAgent = guardPipeline.Apply(
            structuredAgent,
            guardProfile);
        return guardedAgent.AsBuilder()
            .UseOpenTelemetry(
                AITelemetry.AgentSourceName,
                telemetry => telemetry.EnableSensitiveData = enableSensitiveTelemetry)
            .Build();
    }
}
