using System.CommandLine;
using MafPlayground.CLI;
using MafPlayground.CLI.Commands;
using MafPlayground.Retrieval;

namespace MafPlayground.Tests;

public sealed class ParserTests
{
    [Fact]
    public void CreateRootCommand_DiscoversCommandsInConfiguredOrder()
    {
        RootCommand rootCommand = Parser.CreateRootCommand();

        Assert.Equal(
            ["agent", "workflow", "rag", "devui", "docs", "inspect"],
            rootCommand.Subcommands.Select(command => command.Name));
    }

    [Fact]
    public void MetadataOptionParser_RejectsInvalidKeyValueSyntax()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            MetadataOptionParser.Parse(["audience"], "--metadata"));

        Assert.Contains("key=value", exception.Message);
    }

    [Fact]
    public void MetadataOptionParser_NormalizesKeys()
    {
        KnowledgeMetadata metadata = MetadataOptionParser.Parse(
            [" Audience =customer"],
            "--metadata");

        Assert.Equal("customer", metadata.Values["audience"]);
    }

    [Fact]
    public async Task BasicCommand_MapsOptions()
    {
        BasicAgentCommandOptions? captured = null;
        RootCommand rootCommand = CreateRootCommand(AgentCommand.Create(
            (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            }));

        int exitCode = await rootCommand.Parse(
            ["agent", "basic", "--model", "ollama:qwen3:4b", "--prompt", "hello", "--watch"])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new BasicAgentCommandOptions("ollama:qwen3:4b", "hello", Watch: true),
            captured);
    }

    [Fact]
    public async Task DevUICommand_MapsOptions()
    {
        DevUICommandOptions? captured = null;
        RootCommand rootCommand = CreateRootCommand(DevUICommand.Create(
            (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            }));

        int exitCode = await rootCommand.Parse(
            ["devui", "--model", "ollama:qwen3:4b", "--url", "http://localhost:6060"])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new DevUICommandOptions("ollama:qwen3:4b", "http://localhost:6060"),
            captured);
    }

    [Fact]
    public async Task BasicRagCommand_MapsOptionsWithoutGlobalEmbeddingOverride()
    {
        BasicRagAgentCommandOptions? captured = null;
        RootCommand rootCommand = CreateRootCommand(AgentCommand.Create(
            runBasicRagAgentAsync: (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            }));

        int exitCode = await rootCommand.Parse(
            ["agent", "basic-rag", "--model", "ollama:qwen3:4b", "--prompt", "help", "--watch", "--filter", "audience=customer", "--filter", "product=support"])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.NotNull(captured);
        Assert.Equal("ollama:qwen3:4b", captured.Model);
        Assert.Equal("help", captured.Prompt);
        Assert.True(captured.Watch);
        Assert.Equal(["audience=customer", "product=support"], captured.Filters);
    }

    [Fact]
    public async Task RepositoryHelpCommand_MapsOptions()
    {
        RepositoryHelpAgentCommandOptions? captured = null;
        RootCommand rootCommand = CreateRootCommand(AgentCommand.Create(
            runRepositoryHelpAgentAsync: (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            }));

        int exitCode = await rootCommand.Parse(
            [
                "agent", "repository-help",
                "--model", "google:gemini-3.6-flash",
                "--prompt", "How do I run DevUI?",
                "--watch",
            ])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new RepositoryHelpAgentCommandOptions(
                "google:gemini-3.6-flash",
                "How do I run DevUI?",
                Watch: true),
            captured);
    }

    [Fact]
    public async Task GenerateCliReferenceCommand_MapsOutput()
    {
        GenerateCliReferenceCommandOptions? captured = null;
        RootCommand rootCommand = CreateRootCommand(DocsCommand.Create(
            (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            }));

        int exitCode = await rootCommand.Parse(
            [
                "docs", "generate-cli-reference",
                "--output", "docs/repository-help/cli-reference.md",
            ])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new GenerateCliReferenceCommandOptions(
                "docs/repository-help/cli-reference.md"),
            captured);
    }

    [Fact]
    public async Task RagIngestCommand_MapsKnowledgeBase()
    {
        RagIngestCommandOptions? captured = null;
        RootCommand rootCommand = CreateRootCommand(RagCommand.Create(
            ingestAsync: (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            }));

        int exitCode = await rootCommand.Parse(
            [
                "rag", "ingest",
                "--knowledge-base", "Help",
                "--path", "documents/help.pdf",
                "--source-root", "documents",
                "--metadata", "audience=customer",
                "--metadata", "product=support",
            ])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.NotNull(captured);
        Assert.Equal("documents/help.pdf", captured.Path);
        Assert.Equal("documents", captured.SourceRoot);
        Assert.Equal("Help", captured.KnowledgeBase);
        Assert.Equal(["audience=customer", "product=support"], captured.Metadata);
    }

    [Theory]
    [InlineData("rag", "ingest")]
    [InlineData("docs", "generate-cli-reference")]
    public async Task RequiredOptions_AreRejectedBeforeCommandHandler(
        params string[] commandPath)
    {
        bool invoked = false;
        RootCommand rootCommand = commandPath[0] == "rag"
            ? CreateRootCommand(RagCommand.Create(
                ingestAsync: (_, _) =>
                {
                    invoked = true;
                    return Task.FromResult(0);
                }))
            : CreateRootCommand(DocsCommand.Create(
                (_, _) =>
                {
                    invoked = true;
                    return Task.FromResult(0);
                }));

        int exitCode = await rootCommand.Parse(commandPath).InvokeAsync();

        Assert.NotEqual(0, exitCode);
        Assert.False(invoked);
    }

    [Fact]
    public async Task TranslateWorkflowCommand_MapsOptions()
    {
        TranslateWorkflowCommandOptions? captured = null;
        RootCommand rootCommand = CreateRootCommand(WorkflowCommand.Create(
            (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            }));

        int exitCode = await rootCommand.Parse(
            [
                "workflow", "translate",
                "--model", "ollama:qwen3:4b",
                "--text", "Hello",
                "--languages", "es,fr,pt-BR",
                "--watch",
            ])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new TranslateWorkflowCommandOptions(
                "ollama:qwen3:4b",
                "Hello",
                "es,fr,pt-BR",
                Watch: true),
            captured);
    }

    [Fact]
    public async Task InspectWorkflowCommand_MapsOptions()
    {
        InspectCommandOptions? captured = null;
        RootCommand rootCommand = CreateRootCommand(InspectCommand.Create(
            (options, _) =>
            {
                captured = options;
                return Task.FromResult(0);
            }));

        int exitCode = await rootCommand.Parse(
            [
                "inspect", "workflow", "translation-workflow",
                "--view-input",
                "--diagram",
            ])
            .InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new InspectCommandOptions(
                EntityKind: "workflow",
                EntityId: "translation-workflow",
                ViewInput: true,
                Diagram: true),
            captured);
    }

    private static RootCommand CreateRootCommand(Command command) =>
        Parser.CreateRootCommand([new TestCliCommand(command)]);

    private sealed class TestCliCommand(Command command) : ICliCommand
    {
        public int Order => 0;

        public Command Create() => command;
    }
}
