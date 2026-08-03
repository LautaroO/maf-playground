using MafPlayground.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace MafPlayground.Providers.Ollama;

internal sealed class OllamaChatClientProvider : IChatClientProvider
{
    private readonly OllamaProviderOptions _options;

    public OllamaChatClientProvider(IOptions<OllamaProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public string Name => "ollama";

    public IChatClient CreateChatClient(string model) =>
        new OllamaApiClient(_options.Endpoint, model);
}
