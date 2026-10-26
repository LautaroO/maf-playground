using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Agents;
using MafPlayground.AI.Runtime.Sessions;
using Microsoft.Agents.AI;

namespace MafPlayground.AI.Agents.BasicRagAgent;

public sealed record BasicRagAgentRequest(string Message);
public sealed record BasicRagAgentResponse(string Text);

public sealed class BasicRagAgentEntity : IAIEntity<BasicRagAgentRequest, BasicRagAgentResponse>, IMafAgentSource
{
    private readonly Lazy<AIAgent> agent;
    private readonly IAgentSessionStore? sessions;
    public BasicRagAgentEntity(IEnumerable<IAgentSessionStore> sessions, BasicRagAgent? source = null)
        : this(() => GetDefaultAgent(source), sessions) { }

    internal BasicRagAgentEntity(Func<AIAgent> factory, IEnumerable<IAgentSessionStore> sessions)
    {
        agent = new(factory);
        this.sessions = sessions.SingleOrDefault();
    }

    internal static AIAgent GetDefaultAgent(BasicRagAgent? source) => source?.Agent
        ?? throw new InvalidOperationException("The default BasicRagAgent source is not configured. Register AddBasicRagAgent in the host or use a bound execution profile.");

    public AIAgent Agent => agent.Value;

    public async ValueTask<BasicRagAgentResponse> ExecuteAsync(BasicRagAgentRequest input, AIExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Message)) throw new AIEntityInputException("A non-empty message is required.");
        return new(await MafAgentExecution.RunAsync(Agent, "basic-rag-agent", input.Message, context, sessions, cancellationToken).ConfigureAwait(false));
    }
}
