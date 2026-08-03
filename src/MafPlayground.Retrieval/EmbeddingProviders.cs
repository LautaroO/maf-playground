using Microsoft.Extensions.AI;

namespace MafPlayground.Retrieval;

public interface IEmbeddingGeneratorProvider
{
    string Name { get; }
    IEmbeddingGenerator<string, Embedding<float>> Create(string model);
}

public sealed class EmbeddingProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IEmbeddingGeneratorProvider> _providers;

    public EmbeddingProviderRegistry(IEnumerable<IEmbeddingGeneratorProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IEmbeddingGenerator<string, Embedding<float>> Create(EmbeddingModelSelection selection)
    {
        if (!_providers.TryGetValue(selection.Provider, out IEmbeddingGeneratorProvider? provider))
        {
            throw new EmbeddingProviderNotFoundException(selection.Provider, _providers.Keys);
        }

        return provider.Create(selection.Model);
    }
}

public sealed class EmbeddingProviderNotFoundException : InvalidOperationException
{
    public EmbeddingProviderNotFoundException(string provider, IEnumerable<string> available)
        : base($"Embedding provider '{provider}' is not registered. Available providers: {string.Join(", ", available)}.") { }
}
