using Google.GenAI;
using Microsoft.Extensions.Options;

namespace MafPlayground.Providers.Google;

internal sealed class GoogleGenAIClientFactory : IDisposable
{
    private readonly Lazy<Client> _client;

    public GoogleGenAIClientFactory(IOptions<GoogleGenAIProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = new Lazy<Client>(
            () => new Client(apiKey: options.Value.GetApiKey()),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Client GetClient() => _client.Value;

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
