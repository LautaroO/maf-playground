using MafPlayground.AI;

namespace MafPlayground.Tests;

public sealed class AIModelSelectionTests
{
    [Fact]
    public void Parse_SplitsOnlyFirstColon()
    {
        AIModelSelection selection = AIModelSelection.Parse("Ollama:qwen3:4b");

        Assert.Equal("ollama", selection.Provider);
        Assert.Equal("qwen3:4b", selection.Model);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("llama3.1")]
    [InlineData("ollama:")]
    [InlineData(":llama3.1")]
    public void TryParse_RejectsInvalidSelectors(string? value)
    {
        Assert.False(AIModelSelection.TryParse(value, out _));
    }
}
