using MafPlayground.AI.Guards;

namespace MafPlayground.AI.Agents.BasicAgent;

public sealed class BasicAgentOptions
{
    public const string ConfigurationSectionName = "AI:Agents:Basic";

    public string GuardProfile { get; set; } = GuardProfileNames.Default;
}
