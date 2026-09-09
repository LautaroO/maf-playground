using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Contracts;

/// <summary>
/// Decorates a provider-created chat client with host-level cross-cutting behavior.
/// </summary>
public interface IChatClientDecorator
{
    int Order { get; }

    IChatClient Decorate(
        IChatClient chatClient,
        AIModelSelection modelSelection,
        TimeSpan modelCallTimeout);
}

public static class ChatClientDecoratorOrder
{
    public const int ProviderFailure = 25;
    public const int Timeout = 100;
    public const int Budget = 200;
    public const int ContentGuard = 300;
    public const int CostTelemetry = 400;
}
