namespace MafPlayground.AI.Guards;

public sealed class AIGuardOptions
{
    public const string ConfigurationSectionName = "AI:Guards";

    public Dictionary<string, GuardProfileOptions> Profiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [GuardProfileNames.Default] = new(),
        };
}

public static class GuardProfileNames
{
    public const string Default = "Default";
}

public sealed class GuardProfileOptions
{
    public ContentGuardOptions Content { get; set; } = new();

    public BudgetGuardOptions Budget { get; set; } = new();
}

public sealed class ContentGuardOptions
{
    public bool Enabled { get; set; }

    public int MaxInputCharacters { get; set; } = 20_000;

    public GuardAction InputAction { get; set; } = GuardAction.Redact;

    public GuardAction OutputAction { get; set; } = GuardAction.Redact;

    public GuardAction ToolArgumentsAction { get; set; } = GuardAction.Block;

    public GuardAction ToolResultsAction { get; set; } = GuardAction.Redact;

    public GuardAction RetrievedContentAction { get; set; } = GuardAction.Redact;
}

public enum GuardAction
{
    Allow,
    Redact,
    Block,
}

public sealed class BudgetGuardOptions
{
    public bool Enabled { get; set; }

    public BudgetEnforcement Enforcement { get; set; } = BudgetEnforcement.Soft;

    public decimal? MaxCostPerRun { get; set; }

    public string Currency { get; set; } = "USD";

    public int MaxModelCalls { get; set; } = 8;

    public int MaxToolCalls { get; set; } = 8;

    public long MaxInputTokens { get; set; } = 50_000;

    public long MaxOutputTokens { get; set; } = 10_000;

    public int MaxOutputTokensPerCall { get; set; } = 2_048;

    public int EstimatedCharactersPerToken { get; set; } = 4;
}

public enum BudgetEnforcement
{
    Soft,
    Hard,
}

