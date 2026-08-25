using MafPlayground.AI.Agents.HelpAgent;
using MafPlayground.CLI;
using MafPlayground.CLI.Documentation;

namespace MafPlayground.Tests.CLI;

public sealed class SystemCommandLineHelpCapabilityCatalogTests
{
    private readonly SystemCommandLineHelpCapabilityCatalog _catalog = new(
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
        HelpCapability capability = Assert.IsType<HelpCapability>(
            _catalog.Find(commandPath));

        Assert.Contains(
            $"`{expectedInvocation}`",
            capability.EvidenceText,
            StringComparison.Ordinal);
        Assert.Equal(expectedInvocation, Assert.Single(capability.RequiredInlineCode));
    }

    [Fact]
    public void Find_DoesNotInterpretNaturalLanguage()
    {
        Assert.Null(_catalog.Find("How do I run devui?"));
    }

    [Theory]
    [InlineData("¿Cómo ejecuto DevUI localmente?", "devui")]
    [InlineData("What command ingests documents into RAG?", "rag ingest")]
    [InlineData("Run the repository help agent", "agent repository-help")]
    public void Search_ResolvesCommandNamesWithoutLanguageSpecificRules(
        string request,
        string expectedCommandPath)
    {
        HelpCapability capability = Assert.Single(
            _catalog.Search(request, maxResults: 1));

        Assert.Equal(expectedCommandPath, capability.Id);
    }

    [Fact]
    public void Search_DoesNotGuessWhenNoCommandNameMatches()
    {
        Assert.Empty(_catalog.Search("How do I start the local web interface?"));
    }

    [Fact]
    public void Search_DoesNotRouteConceptualDataIngestionQuestionToIngestCommand()
    {
        Assert.Empty(_catalog.Search(
            "Does data ingestion replace our persistence with a vector store?"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Search_RejectsUnboundedResultLimits(int maxResults)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _catalog.Search("devui", maxResults));
    }
}
