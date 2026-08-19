using MafPlayground.CLI;
using MafPlayground.CLI.Documentation;

namespace MafPlayground.Tests.CLI;

public sealed class RepositoryHelpCliReferenceGeneratorTests
{
    [Fact]
    public void GeneratedReference_MatchesCheckedInDocument()
    {
        string expectedPath = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "repository-help-cli-reference.md");
        string expected = File.ReadAllText(expectedPath);

        string actual = RepositoryHelpCliReferenceGenerator.Generate(
            Parser.CreateRootCommand());

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GeneratedReference_ContainsRepositoryHelpAndNoLocalizedFrameworkOptions()
    {
        string actual = RepositoryHelpCliReferenceGenerator.Generate(
            Parser.CreateRootCommand());

        Assert.Contains(
            "`maf-playground agent repository-help`",
            actual,
            StringComparison.Ordinal);
        Assert.Contains(
            "`maf-playground docs generate-cli-reference`",
            actual,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Mostrar ayuda", actual, StringComparison.Ordinal);
    }
}
