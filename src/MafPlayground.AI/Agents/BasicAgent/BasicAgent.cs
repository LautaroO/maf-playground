using MafPlayground.AI.Contracts;
using MafPlayground.AI.Guards;
using MafPlayground.AI.Observability;
using MafPlayground.AI.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Agents.BasicAgent;

public sealed class BasicAgent
{
    public BasicAgent(
        IChatClient chatClient,
        CurrentDateTimeTool currentDateTimeTool,
        UserContextProvider userContextProvider,
        AgentGuardPipeline guardPipeline,
        IOptions<BasicAgentOptions> options,
        IOptions<AgentTelemetryOptions>? telemetryOptions = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(currentDateTimeTool);
        ArgumentNullException.ThrowIfNull(userContextProvider);
        ArgumentNullException.ThrowIfNull(guardPipeline);
        ArgumentNullException.ThrowIfNull(options);

        AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "basic-agent",
            Description = "A basic conversational agent for experimenting with Microsoft Agent Framework.",
            ChatOptions = new ChatOptions
            {
                Instructions = BasicAgentInstructions.Value,
                Tools = [currentDateTimeTool.CreateAIFunction()],
            },
            AIContextProviders = [userContextProvider],
        });

        AIAgent guardedAgent = guardPipeline.Apply(
            agent,
            options.Value.GuardProfile);

        bool enableSensitiveData = telemetryOptions?.Value.EnableSensitiveData ?? false;
        Agent = guardedAgent.AsBuilder()
            .UseOpenTelemetry(
                sourceName: AITelemetry.AgentSourceName,
                configure: telemetry => telemetry.EnableSensitiveData = enableSensitiveData)
            .Build();
    }

    public AIAgent Agent { get; }
}
