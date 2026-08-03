namespace MafPlayground.AI.Workflows.Translation;

public sealed class TranslationWorkflowOptions
{
    public const int DefaultMaxTargetLanguages = 8;
    public const int DefaultMaxInputCharacters = 10_000;

    public string[] DevUITargetLanguages { get; set; } = ["es", "fr", "pt-BR"];

    public int MaxTargetLanguages { get; set; } = DefaultMaxTargetLanguages;

    public int MaxInputCharacters { get; set; } = DefaultMaxInputCharacters;

    public int MaxRepairAttempts { get; set; } = 1;

    public double MinimumValidationConfidence { get; set; } = 0.7;

    public TimeSpan ModelCallTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
