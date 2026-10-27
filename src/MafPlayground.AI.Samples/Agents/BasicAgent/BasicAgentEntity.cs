using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Agents;
using MafPlayground.AI.Runtime.Sessions;
using MafPlayground.AI.Tools;
using MafPlayground.AI.Runtime.Configuration;
using Microsoft.Agents.AI;

namespace MafPlayground.AI.Agents.BasicAgent;

public sealed record BasicAgentRequest(string Message);

public sealed record BasicAgentResponse(string Text);

public sealed class BasicAgentEntity :
    IAIEntity<BasicAgentRequest, BasicAgentResponse>, IMafAgentSource
{
    private readonly IAgentSessionStore? _sessions;
    private readonly IBasicAgentSource source;

    public BasicAgentEntity(
        IBasicAgentSource source,
        IEnumerable<IAgentSessionStore> sessions)
    {
        _sessions = sessions.SingleOrDefault();
        this.source = source;
    }

    public AIAgent Agent => source.Agent;

    public async ValueTask<BasicAgentResponse> ExecuteAsync(
        BasicAgentRequest input,
        AIExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Message))
        {
            throw new AIEntityInputException("A non-empty message is required.");
        }

        return new BasicAgentResponse(await MafAgentExecution.RunAsync(Agent, "basic-agent", input.Message,
            context, _sessions, cancellationToken).ConfigureAwait(false));
    }
}

public interface IBasicAgentSource : IMafAgentSource;

internal sealed class BasicAgentSource : IBasicAgentSource
{
    private readonly Lazy<AIAgent> agent;
    public BasicAgentSource(
        IMafAgentFactory agentFactory,
        CurrentDateTimeTool currentDateTimeTool,
        UserContextProvider userContextProvider,
        ResolvedAIEntityRuntimeOptions? runtimeOptions = null,
        Action<IDisposable>? registerResource = null)
    {
        MafAgentDefinition definition = new("basic-agent", "General conversational agent.", BasicAgentInstructions.Value,
            [currentDateTimeTool.CreateAIFunction()], [userContextProvider]);
        agent = new(() => runtimeOptions is null ? agentFactory.Create("basic-agent", definition)
            : agentFactory.Create("basic-agent", definition, runtimeOptions, registerResource));
    }

    public AIAgent Agent => agent.Value;
}

internal static class BasicAgentInstructions
{
    public const string Value = """
        You are a helpful general-purpose assistant.
        Answer clearly and concisely. If you are uncertain, say so rather than inventing facts.
        Use trusted application context when a request depends on user-specific information.
        Never guess missing user context; ask the user when required information is unavailable.
        Use the available tools when a response depends on information they provide.
        Preserve exact factual values returned by tools. Never modify dates, numbers, identifiers, amounts, or units.
        """;
}
