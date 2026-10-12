using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Agents;
using MafPlayground.AI.Runtime.Sessions;
using MafPlayground.AI.Tools;
using Microsoft.Agents.AI;

namespace MafPlayground.AI.Agents.BasicAgent;

public sealed record BasicAgentRequest(string Message);

public sealed record BasicAgentResponse(string Text);

public sealed class BasicAgentEntity :
    IAIEntity<BasicAgentRequest, BasicAgentResponse>
{
    private readonly IAgentSessionStore? _sessions;

    public BasicAgentEntity(
        IBasicAgentSource source,
        IEnumerable<IAgentSessionStore> sessions)
    {
        _sessions = sessions.SingleOrDefault();
        Agent = source.Agent;
    }

    internal AIAgent Agent { get; }

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

        if (context.SessionId is null)
        {
            AgentResponse response = await Agent.RunAsync(
                input.Message,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return new BasicAgentResponse(response.Text);
        }

        if (_sessions is null)
        {
            throw new InvalidOperationException(
                "Session support requires an explicit IAgentSessionStore registration.");
        }

        AgentSessionKey key = new(
            "basic-agent",
            context.Caller.TenantId,
            context.Caller.SubjectId,
            context.SessionId);
        StoredAgentSession? stored = await _sessions.LoadAsync(key, cancellationToken)
            .ConfigureAwait(false);
        AgentSession session = stored is null
            ? await Agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false)
            : await Agent.DeserializeSessionAsync(stored.State, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        AgentResponse result = await Agent.RunAsync(
            input.Message,
            session,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        System.Text.Json.JsonElement serialized = await Agent.SerializeSessionAsync(
            session,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _ = await _sessions.SaveAsync(
            key,
            serialized,
            stored?.Version,
            cancellationToken).ConfigureAwait(false);
        return new BasicAgentResponse(result.Text);
    }
}

public interface IBasicAgentSource : IMafAgentSource;

internal sealed class BasicAgentSource : IBasicAgentSource
{
    public BasicAgentSource(
        IMafAgentFactory agentFactory,
        CurrentDateTimeTool currentDateTimeTool,
        UserContextProvider userContextProvider)
    {
        Agent = agentFactory.Create(
            "basic-agent",
            new MafAgentDefinition(
                "basic-agent",
                "General conversational agent.",
                BasicAgentInstructions.Value,
                [currentDateTimeTool.CreateAIFunction()],
                [userContextProvider]));
    }

    public AIAgent Agent { get; }
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
