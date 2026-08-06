using Xunit;

namespace MafPlayground.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OllamaCollection : ICollectionFixture<OllamaFixture>
{
    public const string Name = "Ollama";
}

public sealed class OllamaFixture;

public sealed class OllamaContractFactAttribute : FactAttribute
{
    public OllamaContractFactAttribute()
    {
        if (!IsEnabled("RUN_OLLAMA_TESTS"))
        {
            Skip = "Set RUN_OLLAMA_TESTS=true and AI_MODEL=ollama:<model> to run Ollama contract tests.";
        }
    }

    internal static bool IsEnabled(string variable) =>
        bool.TryParse(Environment.GetEnvironmentVariable(variable), out bool enabled) &&
        enabled;
}

public sealed class ModelEvaluationFactAttribute : FactAttribute
{
    public ModelEvaluationFactAttribute()
    {
        if (!OllamaContractFactAttribute.IsEnabled("RUN_MODEL_EVALUATIONS"))
        {
            Skip = "Set RUN_MODEL_EVALUATIONS=true and AI_MODEL=ollama:<model> to run real-model evaluations.";
        }
    }
}
