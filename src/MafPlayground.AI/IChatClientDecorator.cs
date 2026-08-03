using Microsoft.Extensions.AI;

namespace MafPlayground.AI;

/// <summary>
/// Decorates a provider-created chat client with host-level cross-cutting behavior.
/// </summary>
public interface IChatClientDecorator
{
    IChatClient Decorate(IChatClient chatClient, AIModelSelection modelSelection);
}
