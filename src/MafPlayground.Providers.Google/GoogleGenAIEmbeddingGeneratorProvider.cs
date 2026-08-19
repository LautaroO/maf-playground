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

    internal GoogleGenAIEmbeddingGeneratorProvider(Func<Client> getClient)
    {
        _getClient = getClient ?? throw new ArgumentNullException(nameof(getClient));
    }

    public string Name => "google";

    public string GetEmbeddingIdentity(string model, int dimensions)
    {
        EnsureSupported(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        string strategy = model.Equals(
            "gemini-embedding-2",
            StringComparison.OrdinalIgnoreCase)
            ? "retrieval-asymmetric-v1"
            : "retrieval-task-type-v1";
        return $"{Name}:{model.Trim()}/{dimensions}/{strategy}";
    }

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
            new DeferredGoogleEmbeddingGenerator(
                () => _getClient().Models.AsIEmbeddingGenerator(model, dimensions),
                model,
                dimensions);
        return new GoogleEmbeddingPurposeGenerator(inner, model, purpose);
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

    internal sealed class DeferredGoogleEmbeddingGenerator(
        Func<IEmbeddingGenerator<string, Embedding<float>>> create,
        string model,
        int dimensions)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly Lazy<IEmbeddingGenerator<string, Embedding<float>>> _inner =
            new(create, LazyThreadSafetyMode.ExecutionAndPublication);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            _inner.Value.GenerateAsync(values, options, cancellationToken);

        public object? GetService(System.Type serviceType, object? serviceKey = null)
        {
            if (serviceKey is null && serviceType == typeof(EmbeddingGeneratorMetadata))
            {
                return new EmbeddingGeneratorMetadata(
                    "google",
                    null,
                    model,
                    dimensions);
            }

            return _inner.IsValueCreated
                ? _inner.Value.GetService(serviceType, serviceKey)
                : serviceType.IsInstanceOfType(this) && serviceKey is null
                    ? this
                    : null;
        }

        public void Dispose()
        {
            if (_inner.IsValueCreated)
            {
                _inner.Value.Dispose();
            }
        }
    }

    internal sealed class GoogleEmbeddingPurposeGenerator(
        IEmbeddingGenerator<string, Embedding<float>> inner,
        string model,
        EmbeddingPurpose purpose)
        : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner)
    {
        public override Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (model.Equals("gemini-embedding-2", StringComparison.OrdinalIgnoreCase))
            {
                return base.GenerateAsync(
                    values.Select(FormatEmbedding2Input),
                    options,
                    cancellationToken);
            }

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

        private string FormatEmbedding2Input(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return purpose switch
            {
                EmbeddingPurpose.Document => $"title: none | text: {value}",
                EmbeddingPurpose.Query => $"task: search result | query: {value}",
                _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
            };
        }
    }
}
