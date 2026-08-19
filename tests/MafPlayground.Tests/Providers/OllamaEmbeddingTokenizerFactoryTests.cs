using System.Text.Json;
using MafPlayground.Providers.Ollama;
using MafPlayground.Retrieval;
using Microsoft.Extensions.Options;
using OllamaSharp.Models;

namespace MafPlayground.Tests.Providers;

public sealed class OllamaEmbeddingTokenizerFactoryTests
{
    [Theory]
    [InlineData("nomic-embed-text")]
    [InlineData("nomic-embed-text:latest")]
    [InlineData("nomic-embed-text:v1.5")]
    [InlineData("nomic-embed-text:137m-v1.5-fp16")]
    public void Create_BuildsBertTokenizerFromInstalledNomicMetadata(string model)
    {
        string[] vocabulary =
        [
            "[PAD]",
            "[UNK]",
            "[CLS]",
            "[SEP]",
            "[MASK]",
            "hello",
            "world",
            "##s",
        ];
        JsonElement serializedVocabulary = JsonSerializer.SerializeToElement(
            vocabulary);
        ModelInfo modelInfo = new()
        {
            Architecture = "nomic-bert",
            ExtraInfo = new Dictionary<string, object>
            {
                ["tokenizer.ggml.model"] = "bert",
                ["tokenizer.ggml.tokens"] = serializedVocabulary,
            },
        };

        EmbeddingTokenizer tokenizer = OllamaEmbeddingTokenizerFactory.Create(
            model,
            modelInfo);

        Assert.Equal(3, tokenizer.Instance.CountTokens("Hello worlds"));
        Assert.StartsWith(
            "ollama:nomic-embed-text:bert:vocab-sha256:",
            tokenizer.Identity,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_RejectsUnknownModelBeforeCallingOllama()
    {
        OllamaEmbeddingGeneratorProvider provider = new(
            Options.Create(new OllamaProviderOptions
            {
                Endpoint = new Uri("http://127.0.0.1:1"),
            }));

        EmbeddingTokenizerNotSupportedException exception =
            await Assert.ThrowsAsync<EmbeddingTokenizerNotSupportedException>(
                () => provider.CreateTokenizerAsync("mxbai-embed-large").AsTask());

        Assert.Contains("mxbai-embed-large", exception.Message);
        Assert.Contains("nomic-embed-text", exception.Message);
    }

    [Fact]
    public void Create_RejectsMissingVerboseTokenizerMetadata()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => OllamaEmbeddingTokenizerFactory.Create(
                "nomic-embed-text",
                new ModelInfo
                {
                    Architecture = "nomic-bert",
                    ExtraInfo = new Dictionary<string, object>(),
                }));

        Assert.Contains("verbose tokenizer metadata", exception.Message);
    }
}
