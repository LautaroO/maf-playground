using MafPlayground.AI.Runtime.Agents;
using Microsoft.Agents.AI;

namespace MafPlayground.AI.Agents;

/// <summary>Preserves the host-owned default native source without resolving a scoped typed entity.</summary>
internal sealed class SampleAgentSource(Func<AIAgent> create) : IMafAgentSource
{
    private readonly Lazy<AIAgent> agent = new(create);
    public AIAgent Agent => agent.Value;
}
