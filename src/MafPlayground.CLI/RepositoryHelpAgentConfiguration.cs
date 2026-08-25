using MafPlayground.AI.Agents.HelpAgent;

namespace MafPlayground.CLI;

public static class RepositoryHelpAgentConfiguration
{
    public const string ConfigurationSectionName = "AI:Agents:RepositoryHelp";

    public static HelpAgentDefinition Definition { get; } = new(
        "repository-help-agent",
        "A grounded assistant for understanding the MafPlayground repository and its CLI.",
        """
        Explain how to use the MafPlayground repository and CLI clearly and concisely.
        Treat repository architecture, configuration, and CLI usage as the supported domain.
        """);
}
