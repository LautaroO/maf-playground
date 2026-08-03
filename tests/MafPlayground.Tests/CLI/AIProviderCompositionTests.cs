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

    [Fact]
    public void AddConfiguredAIProviders_ExposesConfiguredOllamaPricing()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Providers:Ollama:Pricing:Currency"] = "USD",
                ["AI:Providers:Ollama:Pricing:Version"] = "test",
                ["AI:Providers:Ollama:Pricing:Models:0:Model"] = "llama3.1:8b",
                ["AI:Providers:Ollama:Pricing:Models:0:InputPerMillionTokens"] = "0.01",
                ["AI:Providers:Ollama:Pricing:Models:0:OutputPerMillionTokens"] = "0.02",
            })
            .Build();
        ServiceCollection services = new();
        services.AddConfiguredAIProviders(configuration);

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        IModelPricingSource pricingSource =
            Assert.Single(serviceProvider.GetServices<IModelPricingSource>());

        Assert.True(pricingSource.TryGetPrice("llama3.1:8b", out ModelTokenPrice? price));
        Assert.Equal("USD", price.Currency);
        Assert.Equal("test", price.PricingVersion);
        Assert.Equal(0.01m, price.InputPerMillionTokens);
        Assert.Equal(0.02m, price.OutputPerMillionTokens);
    }

    [Fact]
    public void AddConfiguredAIProviders_RejectsInvalidOllamaPricing()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Providers:Ollama:Pricing:Models:0:Model"] = "llama3.1:8b",
                ["AI:Providers:Ollama:Pricing:Models:0:InputPerMillionTokens"] = "-0.01",
            })
            .Build();
        ServiceCollection services = new();
        services.AddConfiguredAIProviders(configuration);

        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            serviceProvider.GetServices<IModelPricingSource>().ToArray());
    }
}
