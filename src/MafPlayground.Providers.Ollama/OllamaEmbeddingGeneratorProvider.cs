using MafPlayground.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;
using OllamaSharp;

namespace MafPlayground.Providers.Ollama;

internal sealed class OllamaEmbeddingGeneratorProvider(IOptions<OllamaProviderOptions> options) : IEmbeddingGeneratorProvider
{
    public string Name => "ollama";

    public IEmbeddingGenerator<string, Embedding<float>> Create(string model) =>
        new OllamaApiClient(options.Value.Endpoint, model);

    public EmbeddingTokenizer CreateTokenizer(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return new(
            TiktokenTokenizer.CreateForEncoding("cl100k_base"),
            "ollama:approximate-cl100k_base:1.0.1");
    }
}
