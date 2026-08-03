using MafPlayground.AI;
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
                "The Ollama endpoint must be an absolute HTTP or HTTPS URI.");

        serviceCollection.AddSingleton<IChatClientProvider, OllamaChatClientProvider>();
        return serviceCollection;
    }
}
