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
        IOptions<AgentTelemetryOptions>? telemetryOptions = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(currentDateTimeTool);
        ArgumentNullException.ThrowIfNull(userContextProvider);

        AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "basic-agent",
            Description = "A basic conversational agent for experimenting with Microsoft Agent Framework.",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                    You are a helpful general-purpose assistant.
                    Answer clearly and concisely. If you are uncertain, say so rather than inventing facts.
                    Use trusted application context when a request depends on user-specific information.
                    Never guess missing user context; ask the user when required information is unavailable.
                    Use the available tools when a response depends on information they provide.
                    Preserve exact factual values returned by tools. Never modify dates, numbers, identifiers, amounts, or units.
                    """,
                Tools = [currentDateTimeTool.CreateAIFunction()],
            },
            AIContextProviders = [userContextProvider],
        });

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
