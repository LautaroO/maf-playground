using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Guards;

namespace MafPlayground.AI.Agents.HelpAgent;

public sealed class HelpAgentOptions
{
    public string KnowledgeBase { get; set; } = "Help";

    public string GuardProfile { get; set; } = GuardProfileNames.Default;

    public RagRetrievalOptions Retrieval { get; set; } = new();
}
