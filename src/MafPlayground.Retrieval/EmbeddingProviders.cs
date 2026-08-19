using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;

namespace MafPlayground.Retrieval;

public interface IEmbeddingGeneratorProvider
{
    string Name { get; }

    IEmbeddingGenerator<string, Embedding<float>> Create(string model);

    EmbeddingTokenizer CreateTokenizer(string model);
}

public sealed record EmbeddingTokenizer
{
    public EmbeddingTokenizer(Tokenizer instance, string identity)
    {
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        Identity = string.IsNullOrWhiteSpace(identity)
            ? throw new ArgumentException(
                "Tokenizer identity cannot be empty.",
                nameof(identity))
            : identity.Trim();
    }

    public Tokenizer Instance { get; }

    public string Identity { get; }
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
        return GetRequired(selection).Create(selection.Model);
    }

    public EmbeddingTokenizer CreateTokenizer(EmbeddingModelSelection selection) =>
        GetRequired(selection).CreateTokenizer(selection.Model);

    private IEmbeddingGeneratorProvider GetRequired(EmbeddingModelSelection selection) =>
        _providers.TryGetValue(selection.Provider, out IEmbeddingGeneratorProvider? provider)
            ? provider
            : throw new EmbeddingProviderNotFoundException(selection.Provider, _providers.Keys);
}

public sealed class EmbeddingProviderNotFoundException : InvalidOperationException
{
    public EmbeddingProviderNotFoundException(string provider, IEnumerable<string> available)
        : base($"Embedding provider '{provider}' is not registered. Available providers: {string.Join(", ", available)}.") { }
}
