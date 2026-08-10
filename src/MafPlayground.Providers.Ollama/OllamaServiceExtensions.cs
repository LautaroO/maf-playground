using MafPlayground.AI;
using MafPlayground.AI.Contracts;
using MafPlayground.Retrieval;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Providers.Ollama;

public static class OllamaServiceExtensions
{
    public static IServiceCollection AddOllamaProvider(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        ArgumentNullException.ThrowIfNull(configuration);

        serviceCollection
            .AddOptions<OllamaProviderOptions>()
            .Bind(configuration.GetSection(OllamaProviderOptions.ConfigurationSectionName))
            .Validate(
                static options => options.HasValidEndpoint(),
                "The Ollama endpoint must be an absolute HTTP or HTTPS URI.")
            .Validate(
                static options => options.HasValidPricing(),
                "Ollama pricing requires a currency, version, unique model names, and non-negative token prices.")
            .ValidateOnStart();

        serviceCollection.AddSingleton<IChatClientProvider, OllamaChatClientProvider>();
        serviceCollection.AddSingleton<IEmbeddingGeneratorProvider, OllamaEmbeddingGeneratorProvider>();
        serviceCollection.AddSingleton<IModelPricingSource, OllamaModelPricingSource>();
        return serviceCollection;
    }
}
