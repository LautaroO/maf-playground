using Microsoft.Extensions.Configuration;

namespace MafPlayground.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void CliAppSettings_ContainsOllamaCostSample()
    {
        string appSettingsPath = Path.Combine(
            Path.GetDirectoryName(typeof(MafPlayground.CLI.Parser).Assembly.Location)!,
            "appsettings.json");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath, optional: false)
            .Build();

        Assert.True(configuration.GetValue<bool>("Observability:Cost:Enabled"));
        Assert.Equal("USD", configuration["AI:Providers:Ollama:Pricing:Currency"]);
        Assert.Equal("local-ollama-sample", configuration["AI:Providers:Ollama:Pricing:Version"]);
        Assert.Equal("llama3.1:8b", configuration["AI:Providers:Ollama:Pricing:Models:0:Model"]);
        Assert.Equal(
            "0.01",
            configuration["AI:Providers:Ollama:Pricing:Models:0:InputPerMillionTokens"]);
        Assert.Equal(
            "0.01",
            configuration["AI:Providers:Ollama:Pricing:Models:0:OutputPerMillionTokens"]);
    }
}
