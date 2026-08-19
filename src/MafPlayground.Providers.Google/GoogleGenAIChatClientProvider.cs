using Google.GenAI;
using MafPlayground.AI.Contracts;
using Microsoft.Extensions.AI;

namespace MafPlayground.Providers.Google;

internal sealed class GoogleGenAIChatClientProvider : IChatClientProvider
{
    private readonly GoogleGenAIClientFactory _clientFactory;

    public GoogleGenAIChatClientProvider(GoogleGenAIClientFactory clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
    }

    public string Name => "google";

    public IChatClient CreateChatClient(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return _clientFactory.GetClient().Models.AsIChatClient(model);
    }
}
