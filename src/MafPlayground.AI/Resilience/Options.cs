namespace MafPlayground.AI.Resilience;

public sealed class AIResilienceOptions
{
    public const string ConfigurationSectionName = "AI:Resilience";

    public TimeSpan ModelCallTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
