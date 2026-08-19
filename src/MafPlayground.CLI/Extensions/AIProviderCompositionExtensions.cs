using MafPlayground.Providers.Google;
using MafPlayground.Providers.Ollama;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.CLI.Extensions;

public static class AIProviderCompositionExtensions
{
    public static IServiceCollection AddConfiguredAIProviders(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        ArgumentNullException.ThrowIfNull(configuration);

        serviceCollection
            .AddGoogleGenAIProvider(configuration)
            .AddOllamaProvider(configuration);
        return serviceCollection;
    }
}
