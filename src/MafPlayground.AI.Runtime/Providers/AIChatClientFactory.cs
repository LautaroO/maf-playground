using MafPlayground.AI.Contracts;
using MafPlayground.AI.Runtime.Configuration;
using Microsoft.Extensions.AI;

namespace MafPlayground.AI.Runtime.Providers;

public interface IAIChatClientFactory
{
    IChatClient Create(string entityId);

    IChatClient Create(ResolvedAIEntityRuntimeOptions options) =>
        throw new NotSupportedException("This chat-client factory does not support explicit runtime options.");
}

internal sealed class AIChatClientFactory(
    AIProviderRegistry providers,
    IAIEntityRuntimeOptionsResolver runtimeOptions,
    IEnumerable<IChatClientDecorator> decorators) : IAIChatClientFactory
{
    private readonly IChatClientDecorator[] _decorators = ValidateDecorators(decorators);

    public IChatClient Create(string entityId)
    {
        return Create(runtimeOptions.Resolve(entityId));
    }

    public IChatClient Create(ResolvedAIEntityRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        IChatClient client = providers.CreateChatClient(options.Model);
        foreach (IChatClientDecorator decorator in _decorators)
        {
            client = decorator.Decorate(client, options.Model, options.ModelCallTimeout);
        }

        return client;
    }

    private static IChatClientDecorator[] ValidateDecorators(
        IEnumerable<IChatClientDecorator> decorators)
    {
        IChatClientDecorator[] ordered = decorators.OrderBy(item => item.Order).ToArray();
        if (ordered.Select(item => item.Order).Distinct().Count() != ordered.Length)
        {
            throw new InvalidOperationException(
                "Chat-client decorators must have unique explicit order values.");
        }

        return ordered;
    }
}
