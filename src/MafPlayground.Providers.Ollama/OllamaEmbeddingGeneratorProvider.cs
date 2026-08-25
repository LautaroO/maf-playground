using MafPlayground.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace MafPlayground.Providers.Ollama;

internal sealed class OllamaEmbeddingGeneratorProvider(IOptions<OllamaProviderOptions> options) : IEmbeddingGeneratorProvider
{
    public string Name => "ollama";

    public string GetEmbeddingIdentity(string model, int dimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        return $"{Name}:{model.Trim()}/{dimensions}/raw-v1";
    }

    public IEmbeddingGenerator<string, Embedding<float>> Create(
        string model,
        int dimensions,
        EmbeddingPurpose purpose) =>
        new OllamaApiClient(options.Value.Endpoint, model);

}
