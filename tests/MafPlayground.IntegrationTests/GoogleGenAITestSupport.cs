using Xunit;

namespace MafPlayground.IntegrationTests;

public sealed class GoogleGenAIContractFactAttribute : FactAttribute
{
    public GoogleGenAIContractFactAttribute()
    {
        if (!OllamaContractFactAttribute.IsEnabled("RUN_GOOGLE_GENAI_TESTS") ||
            string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("GEMINI_API_KEY")))
        {
            Skip = "Set RUN_GOOGLE_GENAI_TESTS=true and GEMINI_API_KEY to run Google Gen AI contract tests.";
        }
    }
}
