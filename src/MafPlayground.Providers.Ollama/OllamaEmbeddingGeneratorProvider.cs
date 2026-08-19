using MafPlayground.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;

namespace MafPlayground.Providers.Ollama;

internal sealed class OllamaEmbeddingGeneratorProvider(IOptions<OllamaProviderOptions> options) : IEmbeddingGeneratorProvider
{
    public string Name => "ollama";

    public IEmbeddingGenerator<string, Embedding<float>> Create(string model) =>
        new OllamaApiClient(options.Value.Endpoint, model);

    public async ValueTask<EmbeddingTokenizer> CreateTokenizerAsync(
        string model,
        CancellationToken cancellationToken = default)
    {
        OllamaEmbeddingTokenizerFactory.EnsureSupported(model);
        using OllamaApiClient client = new(options.Value.Endpoint, model);
        ShowModelResponse response = await client.ShowModelAsync(
            new ShowModelRequest
            {
                Model = model,
                Verbose = true,
            },
            cancellationToken);
        return OllamaEmbeddingTokenizerFactory.Create(model, response.Info);
    }
}
