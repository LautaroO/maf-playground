using MafPlayground.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace MafPlayground.Providers.Ollama;

internal sealed class OllamaEmbeddingGeneratorProvider(IOptions<OllamaProviderOptions> options) : IEmbeddingGeneratorProvider
{
    public string Name => "ollama";

    public IEmbeddingGenerator<string, Embedding<float>> Create(string model) =>
        new OllamaApiClient(options.Value.Endpoint, model);
}
