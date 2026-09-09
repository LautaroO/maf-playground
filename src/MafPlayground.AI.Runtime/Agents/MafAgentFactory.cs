using MafPlayground.AI.Guards;
using MafPlayground.AI.Observability;
using MafPlayground.AI.Runtime.Configuration;
using MafPlayground.AI.Runtime.Providers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Runtime.Agents;

public sealed record MafAgentDefinition(
    string Name,
    string Description,
    string Instructions,
    IReadOnlyList<AITool>? Tools = null,
    IReadOnlyList<AIContextProvider>? ContextProviders = null);

public interface IMafAgentFactory
{
    AIAgent Create(string entityId, MafAgentDefinition definition);
}

public interface IMafAgentPipeline
{
    AIAgent Apply(string entityId, AIAgent agent);
}

public interface IMafAgentSource
{
    AIAgent Agent { get; }
}

internal sealed class MafAgentFactory(
    IAIChatClientFactory chatClients,
    IMafAgentPipeline pipeline) : IMafAgentFactory
{
    public AIAgent Create(string entityId, MafAgentDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentNullException.ThrowIfNull(definition);
        IChatClient chatClient = chatClients.Create(entityId);
        AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = definition.Name,
            Description = definition.Description,
            ChatOptions = new ChatOptions
            {
                Instructions = definition.Instructions,
                Tools = definition.Tools?.ToList(),
            },
            AIContextProviders = definition.ContextProviders?.ToList(),
        });
        return pipeline.Apply(entityId, agent);
    }
}

internal sealed class MafAgentPipeline(
    AgentGuardPipeline guards,
    IAIEntityRuntimeOptionsResolver runtimeOptions,
    IOptions<AgentTelemetryOptions> telemetryOptions) : IMafAgentPipeline
{
    public AIAgent Apply(string entityId, AIAgent agent)
    {
        ResolvedAIEntityRuntimeOptions options = runtimeOptions.Resolve(entityId);
        AIAgent guarded = guards.Apply(agent, options.GuardProfile);
        return guarded.AsBuilder()
            .UseOpenTelemetry(
                sourceName: AITelemetry.AgentSourceName,
                configure: telemetry =>
                    telemetry.EnableSensitiveData = telemetryOptions.Value.EnableSensitiveData)
            .Build();
    }
}
