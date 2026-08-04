namespace MafPlayground.AI.Workflows.Translation;

public sealed class TranslationWorkflowOptions
{
    public const int DefaultMaxTargetLanguages = 8;
    public const int DefaultMaxInputCharacters = 10_000;

    public string[] SupportedTargetLanguages { get; set; } = ["es", "fr", "pt-BR"];

    public int MaxTargetLanguages { get; set; } = DefaultMaxTargetLanguages;

    public int MaxInputCharacters { get; set; } = DefaultMaxInputCharacters;

    public int MaxTranslationRetries { get; set; } = 1;

    public double MinimumValidationConfidence { get; set; } = 0.7;
}
