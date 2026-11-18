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

    AIAgent Create(string entityId, MafAgentDefinition definition, ResolvedAIEntityRuntimeOptions options, Action<IDisposable>? registerResource = null);
}

public interface IMafAgentPipeline
{
    AIAgent Apply(string entityId, AIAgent agent);

    AIAgent Apply(string entityId, AIAgent agent, ResolvedAIEntityRuntimeOptions options);
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
        return pipeline.Apply(entityId, CreateAgent(chatClients.Create(entityId), definition));
    }

    public AIAgent Create(string entityId, MafAgentDefinition definition, ResolvedAIEntityRuntimeOptions options, Action<IDisposable>? registerResource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentNullException.ThrowIfNull(definition);
        IChatClient chatClient = chatClients.Create(options);
        registerResource?.Invoke(chatClient);
        AIAgent agent = CreateAgent(chatClient, definition);
        AIAgent result = pipeline.Apply(entityId, agent, options);
        if (result is IDisposable disposable) registerResource?.Invoke(disposable);
        return result;
    }

    private static AIAgent CreateAgent(IChatClient chatClient, MafAgentDefinition definition) =>
        chatClient.AsAIAgent(new ChatClientAgentOptions
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
}

internal sealed class MafAgentPipeline(
    AgentGuardPipeline guards,
    IAIEntityRuntimeOptionsResolver runtimeOptions,
    IOptions<AgentTelemetryOptions> telemetryOptions) : IMafAgentPipeline
{
    public AIAgent Apply(string entityId, AIAgent agent)
    {
        return Apply(entityId, agent, runtimeOptions.Resolve(entityId));
    }

    public AIAgent Apply(string entityId, AIAgent agent, ResolvedAIEntityRuntimeOptions options)
    {
        AIAgent guarded = guards.Apply(agent, options.GuardProfile);
        return guarded.AsBuilder()
            .UseOpenTelemetry(
                sourceName: AITelemetry.AgentSourceName,
                configure: telemetry =>
                    telemetry.EnableSensitiveData = telemetryOptions.Value.EnableSensitiveData)
            .Build();
    }
}
