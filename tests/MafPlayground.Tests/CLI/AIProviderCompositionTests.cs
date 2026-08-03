using MafPlayground.AI;
using MafPlayground.CLI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MafPlayground.Tests;

public sealed class AIProviderCompositionTests
{
    [Fact]
    public void AddConfiguredAIProviders_RegistersOllama()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddConfiguredAIProviders(configuration);

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        AIProviderRegistry registry = new(serviceProvider.GetServices<IChatClientProvider>());

        Assert.Contains("ollama", registry.ProviderNames);
    }

    [Fact]
    public void AddConfiguredAIProviders_RejectsInvalidOllamaEndpoint()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Providers:Ollama:Endpoint"] = "not-a-uri"
            })
            .Build();
        ServiceCollection services = new();
        services.AddConfiguredAIProviders(configuration);

        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            serviceProvider.GetServices<IChatClientProvider>().ToArray());
    }
}
