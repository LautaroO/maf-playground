using Google.GenAI;
using Google.GenAI.Types;
using MafPlayground.Retrieval;
using Microsoft.Extensions.AI;

namespace MafPlayground.Providers.Google;

internal sealed class GoogleGenAIEmbeddingGeneratorProvider
    : IEmbeddingGeneratorProvider
{
    private static readonly string[] SupportedModels =
    [
        "gemini-embedding-2",
        "gemini-embedding-001",
    ];
    private readonly Func<Client> _getClient;

    public GoogleGenAIEmbeddingGeneratorProvider(
        GoogleGenAIClientFactory clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _getClient = clientFactory.GetClient;
    }

    internal GoogleGenAIEmbeddingGeneratorProvider(Client client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _getClient = () => client;
    }

    public string Name => "google";

    public IEmbeddingGenerator<string, Embedding<float>> Create(
        string model,
        int dimensions,
        EmbeddingPurpose purpose)
    {
        EnsureSupported(model);
        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dimensions),
                "Embedding dimensions must be greater than zero.");
        }

        IEmbeddingGenerator<string, Embedding<float>> inner =
            _getClient().Models.AsIEmbeddingGenerator(model, dimensions);
        return new GoogleEmbeddingPurposeGenerator(inner, purpose);
    }

    public ValueTask<EmbeddingTokenizer> CreateTokenizerAsync(
        string model,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSupported(model);
        return ValueTask.FromResult<EmbeddingTokenizer>(
            new GoogleRemoteEmbeddingTokenizer(_getClient().Models, model));
    }

    private static void EnsureSupported(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (!SupportedModels.Contains(
                model.Trim(),
                StringComparer.OrdinalIgnoreCase))
        {
            throw new EmbeddingTokenizerNotSupportedException(
                "google",
                model,
                SupportedModels);
        }
    }

    internal sealed class GoogleEmbeddingPurposeGenerator(
        IEmbeddingGenerator<string, Embedding<float>> inner,
        EmbeddingPurpose purpose)
        : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner)
    {
        public override Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            EmbeddingGenerationOptions effective = options?.Clone() ?? new();
            Func<IEmbeddingGenerator, object?>? existingFactory =
                effective.RawRepresentationFactory;
            effective.RawRepresentationFactory = generator =>
            {
                EmbedContentConfig config =
                    existingFactory?.Invoke(generator) as EmbedContentConfig ?? new();
                config.TaskType ??= purpose switch
                {
                    EmbeddingPurpose.Document => "RETRIEVAL_DOCUMENT",
                    EmbeddingPurpose.Query => "RETRIEVAL_QUERY",
                    _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
                };
                return config;
            };
            return base.GenerateAsync(values, effective, cancellationToken);
        }
    }
}
