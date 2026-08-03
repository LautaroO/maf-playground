using Microsoft.Extensions.AI;

namespace MafPlayground.AI;

public sealed class AIProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IChatClientProvider> _providers;

    public AIProviderRegistry(IEnumerable<IChatClientProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        Dictionary<string, IChatClientProvider> providerMap = new(StringComparer.OrdinalIgnoreCase);

        foreach (IChatClientProvider provider in providers)
        {
            if (!providerMap.TryAdd(provider.Name, provider))
            {
                throw new InvalidOperationException(
                    $"The AI provider '{provider.Name}' has been registered more than once.");
            }
        }

        _providers = providerMap;
    }

    public IReadOnlyCollection<string> ProviderNames =>
        _providers.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public IChatClient CreateChatClient(AIModelSelection modelSelection)
    {
        ArgumentNullException.ThrowIfNull(modelSelection);

        if (!_providers.TryGetValue(modelSelection.Provider, out IChatClientProvider? provider))
        {
            throw new AIProviderNotFoundException(modelSelection.Provider, ProviderNames);
        }

        return provider.CreateChatClient(modelSelection.Model);
    }
}

public sealed class AIProviderNotFoundException : InvalidOperationException
{
    public AIProviderNotFoundException(string provider, IEnumerable<string> availableProviders)
        : base(CreateMessage(provider, availableProviders))
    {
        Provider = provider;
    }

    public string Provider { get; }

    private static string CreateMessage(string provider, IEnumerable<string> availableProviders)
    {
        string available = string.Join(", ", availableProviders);
        return available.Length == 0
            ? $"AI provider '{provider}' is not registered. No AI providers are available."
            : $"AI provider '{provider}' is not registered. Available providers: {available}.";
    }
}
