using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MafPlayground.AI.Agents.BasicAgent;

public sealed class BasicAgent
{
    public BasicAgent(
        IChatClient chatClient,
        IOptions<AgentTelemetryOptions>? telemetryOptions = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        AIAgent agent = chatClient.AsAIAgent(
            name: "basic-agent",
            instructions: """
                You are a helpful general-purpose assistant.
                Answer clearly and concisely. If you are uncertain, say so rather than inventing facts.
                """,
            description: "A basic conversational agent for experimenting with Microsoft Agent Framework.");

        bool enableSensitiveData = telemetryOptions?.Value.EnableSensitiveData ?? false;
        Agent = agent
            .AsBuilder()
            .UseOpenTelemetry(
                sourceName: AITelemetry.AgentSourceName,
                configure: telemetry => telemetry.EnableSensitiveData = enableSensitiveData)
            .Build();
    }

    public AIAgent Agent { get; }
}
