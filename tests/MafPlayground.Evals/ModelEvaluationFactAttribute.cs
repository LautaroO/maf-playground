using Xunit;

namespace MafPlayground.Evals;

public sealed class ModelEvaluationFactAttribute : FactAttribute
{
    public ModelEvaluationFactAttribute()
    {
        if (!bool.TryParse(
                Environment.GetEnvironmentVariable("RUN_MODEL_EVALUATIONS"),
                out bool enabled) || !enabled)
        {
            Skip = "Set RUN_MODEL_EVALUATIONS=true and AI_MODEL=provider:model to run real-model evaluations.";
        }
    }
}
