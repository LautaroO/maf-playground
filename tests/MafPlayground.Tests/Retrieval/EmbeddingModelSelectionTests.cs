using MafPlayground.Retrieval;

namespace MafPlayground.Tests.Retrieval;

public sealed class EmbeddingModelSelectionTests
{
    [Fact]
    public void TryParse_PreservesColonInModelName()
    {
        bool parsed = EmbeddingModelSelection.TryParse("ollama:nomic-embed-text:latest", out EmbeddingModelSelection? selection);

        Assert.True(parsed);
        Assert.Equal("ollama", selection!.Provider);
        Assert.Equal("nomic-embed-text:latest", selection.Model);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ollama")]
    [InlineData(":model")]
    [InlineData("provider:")]
    public void TryParse_RejectsInvalidSelectors(string? value) =>
        Assert.False(EmbeddingModelSelection.TryParse(value, out _));
}
