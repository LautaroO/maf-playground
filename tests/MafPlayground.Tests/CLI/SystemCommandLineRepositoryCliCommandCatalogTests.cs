using MafPlayground.AI.Agents.RepositoryHelpAgent;
using MafPlayground.CLI;
using MafPlayground.CLI.Documentation;

namespace MafPlayground.Tests.CLI;

public sealed class SystemCommandLineRepositoryCliCommandCatalogTests
{
    private readonly SystemCommandLineRepositoryCliCommandCatalog _catalog = new(
        Parser.CreateRootCommand());

    [Theory]
    [InlineData("devui", "dotnet run --project src/MafPlayground.CLI -- devui")]
    [InlineData(
        "agent repository-help",
        "dotnet run --project src/MafPlayground.CLI -- agent repository-help")]
    [InlineData(
        "rag ingest",
        "dotnet run --project src/MafPlayground.CLI -- rag ingest --path <value> --knowledge-base <value>")]
    public void Find_ReturnsExactInvocationFromCommandPath(
        string commandPath,
        string expectedInvocation)
    {
        RepositoryCliCommand command = Assert.IsType<RepositoryCliCommand>(
            _catalog.Find(commandPath));

        Assert.Equal(expectedInvocation, command.Invocation);
    }

    [Fact]
    public void Find_DoesNotInterpretNaturalLanguage()
    {
        Assert.Null(_catalog.Find("How do I run devui?"));
    }
}
