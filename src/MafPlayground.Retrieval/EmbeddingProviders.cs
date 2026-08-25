using Microsoft.Extensions.AI;
namespace MafPlayground.Retrieval;

public interface IEmbeddingGeneratorProvider
{
    string Name { get; }

    string GetEmbeddingIdentity(string model, int dimensions);

    IEmbeddingGenerator<string, Embedding<float>> Create(
        string model,
        int dimensions,
        EmbeddingPurpose purpose);

}

public enum EmbeddingPurpose
{
    Document,
    Query,
}

public sealed class EmbeddingProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IEmbeddingGeneratorProvider> _providers;

    public EmbeddingProviderRegistry(IEnumerable<IEmbeddingGeneratorProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IEmbeddingGenerator<string, Embedding<float>> Create(
        EmbeddingModelSelection selection,
        int dimensions,
        EmbeddingPurpose purpose)
    {
        return GetRequired(selection).Create(
            selection.Model,
            dimensions,
            purpose);
    }

    public string GetEmbeddingIdentity(
        EmbeddingModelSelection selection,
        int dimensions) =>
        GetRequired(selection).GetEmbeddingIdentity(selection.Model, dimensions);

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

public sealed class EmbeddingModelNotSupportedException : NotSupportedException
{
    public EmbeddingModelNotSupportedException(
        string provider,
        string model,
        IEnumerable<string> supportedModels)
        : base(
            $"Embedding provider '{provider}' does not support model '{model}'. " +
            $"Supported models: {string.Join(", ", supportedModels)}.")
    {
    }
}
