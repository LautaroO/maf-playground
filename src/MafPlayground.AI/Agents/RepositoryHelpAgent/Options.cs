using MafPlayground.AI.Agents.BasicRagAgent;
using MafPlayground.AI.Guards;

namespace MafPlayground.AI.Agents.RepositoryHelpAgent;

public sealed class RepositoryHelpAgentOptions
{
    public const string ConfigurationSectionName = "AI:Agents:RepositoryHelp";

    public string KnowledgeBase { get; set; } = "RepositoryHelp";

    public string GuardProfile { get; set; } = GuardProfileNames.Default;

    public RagRetrievalOptions Retrieval { get; set; } = new();
}
