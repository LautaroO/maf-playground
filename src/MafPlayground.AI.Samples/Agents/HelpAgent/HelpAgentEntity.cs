using MafPlayground.AI.Abstractions;
using MafPlayground.AI.Runtime.Agents;
using MafPlayground.AI.Runtime.Sessions;
using Microsoft.Agents.AI;

namespace MafPlayground.AI.Agents.HelpAgent;

public sealed record HelpAgentRequest(string Message);
public sealed record HelpAgentResponse(string Text);

public sealed class HelpAgentEntity : IAIEntity<HelpAgentRequest, HelpAgentResponse>, IMafAgentSource
{
    private readonly Lazy<AIAgent> agent;
    private readonly IAgentSessionStore? sessions;
    public HelpAgentEntity(HelpAgent source, IEnumerable<IAgentSessionStore> sessions)
        : this(() => source.Agent, sessions) { }

    internal HelpAgentEntity(Func<AIAgent> factory, IEnumerable<IAgentSessionStore> sessions)
    {
        agent = new(factory);
        this.sessions = sessions.SingleOrDefault();
    }

    public AIAgent Agent => agent.Value;

    public async ValueTask<HelpAgentResponse> ExecuteAsync(HelpAgentRequest input, AIExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Message)) throw new AIEntityInputException("A non-empty message is required.");
        return new(await MafAgentExecution.RunAsync(Agent, "repository-help-agent", input.Message, context, sessions, cancellationToken).ConfigureAwait(false));
    }
}
