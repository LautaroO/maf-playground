namespace MafPlayground.AI.Agents.HelpAgent;

public sealed record HelpAgentDefinition
{
    public HelpAgentDefinition(
        string name,
        string description,
        string instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        Name = name;
        Description = description;
        Instructions = instructions;
    }

    public string Name { get; }

    public string Description { get; }

    public string Instructions { get; }
}
