namespace MafPlayground.AI.Agents.HelpAgent;

public interface IHelpCapabilityCatalog
{
    IReadOnlyList<string> CapabilityIds { get; }

    HelpCapability? Find(string capabilityId);

    IReadOnlyList<HelpCapability> Search(
        string request,
        int maxResults = 3);
}

public sealed record HelpCapability
{
    public HelpCapability(
        string id,
        string evidenceText,
        string citation,
        IReadOnlyList<string>? requiredInlineCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(citation);
        if (requiredInlineCode?.Any(string.IsNullOrWhiteSpace) == true)
        {
            throw new ArgumentException(
                "Required inline-code values cannot be empty.",
                nameof(requiredInlineCode));
        }

        Id = id;
        EvidenceText = evidenceText;
        Citation = citation;
        RequiredInlineCode = requiredInlineCode?.ToArray() ?? [];
    }

    public string Id { get; }

    public string EvidenceText { get; }

    public string Citation { get; }

    public IReadOnlyList<string> RequiredInlineCode { get; }
}
