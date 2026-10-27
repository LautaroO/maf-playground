using Google.GenAI;
using MafPlayground.AI.Abstractions;

namespace MafPlayground.Providers.Google;

internal sealed class GoogleGenAIExceptionDiagnostics : IAIProviderExceptionDiagnostics
{
    public AIProviderFailureDetails? Describe(Exception exception)
    {
        // SDK 1.18.0 hides HttpRequestException.StatusCode and leaves the base property null.
        int? status = exception switch
        {
            ClientError failure => failure.StatusCode,
            ServerError failure => failure.StatusCode,
            _ => null,
        };
        return status is >= 100 and <= 599 ? new(status) : null;
    }
}
