using Microsoft.Extensions.AI;
using Microsoft.ML.Tokenizers;

namespace MafPlayground.Retrieval;

public interface IEmbeddingGeneratorProvider
{
    string Name { get; }

    IEmbeddingGenerator<string, Embedding<float>> Create(
        string model,
        int dimensions,
        EmbeddingPurpose purpose);

    ValueTask<EmbeddingTokenizer> CreateTokenizerAsync(
        string model,
        CancellationToken cancellationToken = default);
}

public enum EmbeddingPurpose
{
    Document,
    Query,
}

public sealed record EmbeddingTokenBoundary(int Index, int TokenCount);

public abstract class EmbeddingTokenizer
{
    protected EmbeddingTokenizer(string identity)
    {
        Identity = string.IsNullOrWhiteSpace(identity)
            ? throw new ArgumentException(
                "Tokenizer identity cannot be empty.",
                nameof(identity))
            : identity.Trim();
    }

    public string Identity { get; }

    public abstract ValueTask<int> CountTokensAsync(
        string text,
        CancellationToken cancellationToken = default);

    public abstract ValueTask<EmbeddingTokenBoundary> GetPrefixBoundaryAsync(
        string text,
        int maxTokens,
        CancellationToken cancellationToken = default);

    public abstract ValueTask<EmbeddingTokenBoundary> GetSuffixBoundaryAsync(
        string text,
        int maxTokens,
        CancellationToken cancellationToken = default);
}

public sealed class LocalEmbeddingTokenizer : EmbeddingTokenizer
{
    public LocalEmbeddingTokenizer(Tokenizer instance, string identity)
        : base(identity)
    {
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    public Tokenizer Instance { get; }

    public override ValueTask<int> CountTokensAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Instance.CountTokens(
            text,
            considerNormalization: true));
    }

    public override ValueTask<EmbeddingTokenBoundary> GetPrefixBoundaryAsync(
        string text,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int index = Instance.GetIndexByTokenCount(
            text,
            maxTokens,
            out string? _,
            out int _,
            considerNormalization: true);
        return ValueTask.FromResult(new EmbeddingTokenBoundary(
            index,
            Instance.CountTokens(
                text.AsSpan(0, index),
                considerNormalization: true)));
    }

    public override ValueTask<EmbeddingTokenBoundary> GetSuffixBoundaryAsync(
        string text,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int index = Instance.GetIndexByTokenCountFromEnd(
            text,
            maxTokens,
            out string? _,
            out int _,
            considerNormalization: true);
        return ValueTask.FromResult(new EmbeddingTokenBoundary(
            index,
            Instance.CountTokens(
                text.AsSpan(index),
                considerNormalization: true)));
    }
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

    public ValueTask<EmbeddingTokenizer> CreateTokenizerAsync(
        EmbeddingModelSelection selection,
        CancellationToken cancellationToken = default) =>
        GetRequired(selection).CreateTokenizerAsync(
            selection.Model,
            cancellationToken);

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

public sealed class EmbeddingTokenizerNotSupportedException : NotSupportedException
{
    public EmbeddingTokenizerNotSupportedException(
        string provider,
        string model,
        IEnumerable<string> supportedModels)
        : base(
            $"Embedding provider '{provider}' does not have a tokenizer for model " +
            $"'{model}'. Supported models: {string.Join(", ", supportedModels)}.")
    {
    }
}
