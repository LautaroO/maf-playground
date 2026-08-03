namespace MafPlayground.Observability;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool Enabled { get; set; }

    public string ServiceName { get; set; } = "maf-playground";

    public CostTrackingOptions Cost { get; set; } = new();
}

public sealed class CostTrackingOptions
{
    public bool Enabled { get; set; }
}
