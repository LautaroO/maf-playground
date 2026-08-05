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

    [Fact]
    public void CliAppSettings_ContainsTranslationWorkflowDefaults()
    {
        string appSettingsPath = Path.Combine(
            Path.GetDirectoryName(typeof(MafPlayground.CLI.Parser).Assembly.Location)!,
            "appsettings.json");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath, optional: false)
            .Build();

        Assert.Equal(8, configuration.GetValue<int>(
            "AI:Workflows:Translation:MaxTargetLanguages"));
        Assert.Equal(1, configuration.GetValue<int>(
            "AI:Workflows:Translation:MaxTranslationRetries"));
        string[]? supportedTargetLanguages = configuration
            .GetSection("AI:Workflows:Translation:SupportedTargetLanguages")
            .Get<string[]>();
        Assert.NotNull(supportedTargetLanguages);
        Assert.Equal(["es", "fr", "pt-BR"], supportedTargetLanguages);
        Assert.Equal(TimeSpan.FromMinutes(1), configuration.GetValue<TimeSpan>(
            "AI:Resilience:ModelCallTimeout"));
    }

    [Fact]
    public void CliAppSettings_ConfiguresBasicRagKnowledgeBaseAndSearchPolicy()
    {
        string appSettingsPath = Path.Combine(
            Path.GetDirectoryName(typeof(MafPlayground.CLI.Parser).Assembly.Location)!,
            "appsettings.json");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath, optional: false)
            .Build();

        Assert.Equal("Help", configuration["AI:Agents:BasicRag:KnowledgeBase"]);
        Assert.Equal(5, configuration.GetValue<int>(
            "AI:Agents:BasicRag:Retrieval:TopK"));
        Assert.Empty(configuration
            .GetSection("AI:Agents:BasicRag:Retrieval:MetadataFilters")
            .GetChildren());
        Assert.Equal("basic-rag", configuration["AI:KnowledgeBases:Help:Collection"]);
        Assert.Equal(
            "ollama:nomic-embed-text",
            configuration["AI:KnowledgeBases:Help:EmbeddingModel"]);
        Assert.Equal(768, configuration.GetValue<int>(
            "AI:KnowledgeBases:Help:EmbeddingDimensions"));
    }
}
