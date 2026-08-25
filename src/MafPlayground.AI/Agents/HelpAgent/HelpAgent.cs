using Microsoft.Agents.AI;

namespace MafPlayground.AI.Agents.HelpAgent;

public sealed class HelpAgent
{
    public HelpAgent(IHelpAgentFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Agent = factory.Create() ?? throw new InvalidOperationException(
            "The help-agent factory returned no agent.");
    }

    public AIAgent Agent { get; }
}
