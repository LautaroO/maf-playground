namespace MafPlayground.AI.Abstractions;

/// <summary>Safe, provider-neutral diagnostic metadata; does not prescribe retry behavior.</summary>
public sealed record AIProviderFailureDetails(int? HttpStatusCode);

/// <summary>Reads safe metadata at a provider boundary without changing the failure.</summary>
public interface IAIProviderExceptionDiagnostics
{
    AIProviderFailureDetails? Describe(Exception exception);
}
