namespace MafPlayground.Studio.Contracts;

public sealed record StudioInstance(string HostInstanceId, StudioSourceProvenance Provenance);

/// <summary>Optional startup checkout metadata, never an exact source snapshot.</summary>
public sealed record StudioSourceProvenance
{
    public string? Revision { get; }
    public bool? Dirty { get; }
    public StudioSourceProvenance(string? revision = null, bool? dirty = null)
    {
        Revision = revision is { Length: 40 or 64 } && revision.All(char.IsAsciiHexDigit) ? revision.ToLowerInvariant() : null;
        Dirty = Revision is null ? null : dirty;
    }
}
