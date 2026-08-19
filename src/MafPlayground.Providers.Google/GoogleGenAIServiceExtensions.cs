using MafPlayground.AI.Contracts;
using MafPlayground.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Providers.Google;

public static class GoogleGenAIServiceExtensions
{
    public static IServiceCollection AddGoogleGenAIProvider(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        ArgumentNullException.ThrowIfNull(configuration);

        serviceCollection
            .AddOptions<GoogleGenAIProviderOptions>()
            .Bind(configuration.GetSection(
                GoogleGenAIProviderOptions.ConfigurationSectionName));
        serviceCollection.AddSingleton<GoogleGenAIClientFactory>();
        serviceCollection.AddSingleton<IChatClientProvider,
            GoogleGenAIChatClientProvider>();
        serviceCollection.AddSingleton<IEmbeddingGeneratorProvider,
            GoogleGenAIEmbeddingGeneratorProvider>();
        return serviceCollection;
    }
}
