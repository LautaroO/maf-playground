using MafPlayground.CLI;

namespace MafPlayground.Tests;

public sealed class ParserTests
{
    [Fact]
    public async Task BasicCommand_MapsOptions()
    {
        BasicAgentCommandOptions? captured = null;
        var rootCommand = Parser.CreateRootCommand((options, _) =>
        {
            captured = options;
            return Task.FromResult(0);
        });

        int exitCode = await rootCommand.Parse(
            ["agent", "basic", "--model", "ollama:qwen3:4b", "--prompt", "hello"])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new BasicAgentCommandOptions("ollama:qwen3:4b", "hello"),
            captured);
    }
}
