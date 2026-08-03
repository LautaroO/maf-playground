using MafPlayground.CLI;
using MafPlayground.CLI.Commands;

namespace MafPlayground.Tests;

public sealed class ParserTests
{
    [Fact]
    public async Task BasicCommand_MapsOptions()
    {
        BasicAgentCommandOptions? captured = null;
        var rootCommand = Parser.CreateRootCommand(
            (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            },
            (_, _) => Task.FromResult(0));

        int exitCode = await rootCommand.Parse(
            ["agent", "basic", "--model", "ollama:qwen3:4b", "--prompt", "hello"])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new BasicAgentCommandOptions("ollama:qwen3:4b", "hello"),
            captured);
    }

    [Fact]
    public async Task DevUICommand_MapsOptions()
    {
        DevUICommandOptions? captured = null;
        var rootCommand = Parser.CreateRootCommand(
            (_, _) => Task.FromResult(0),
            (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            });

        int exitCode = await rootCommand.Parse(
            ["devui", "--model", "ollama:qwen3:4b", "--url", "http://localhost:6060"])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new DevUICommandOptions("ollama:qwen3:4b", "http://localhost:6060"),
            captured);
    }

    [Fact]
    public async Task TranslateWorkflowCommand_MapsOptions()
    {
        TranslateWorkflowCommandOptions? captured = null;
        var rootCommand = Parser.CreateRootCommand(
            (_, _) => Task.FromResult(0),
            (_, _) => Task.FromResult(0),
            (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            });

        int exitCode = await rootCommand.Parse(
            [
                "workflow", "translate",
                "--model", "ollama:qwen3:4b",
                "--text", "Hello",
                "--languages", "es,fr,pt-BR",
            ])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new TranslateWorkflowCommandOptions(
                "ollama:qwen3:4b",
                "Hello",
                "es,fr,pt-BR"),
            captured);
    }
}
