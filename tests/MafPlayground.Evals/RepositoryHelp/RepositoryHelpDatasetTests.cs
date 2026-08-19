using Xunit;

namespace MafPlayground.Evals.RepositoryHelp;

public sealed class RepositoryHelpDatasetTests
{
    [Fact]
    public async Task Dataset_IsValidAndCoversCoreBehaviorCategories()
    {
        IReadOnlyList<RepositoryHelpEvalCase> cases = await
            RepositoryHelpEvalDataset.LoadAsync(GetDatasetPath());

        Assert.Contains(cases, item => item.Category == "cli");
        Assert.Contains(cases, item => item.Category == "architecture");
        Assert.Contains(cases, item => item.Category == "ingestion");
        Assert.Contains(cases, item => item.Category == "unsupported");
        Assert.Contains(cases, item => item.ExpectedLanguage == "es");
        Assert.Contains(cases, item => item.ExpectedLanguage == "en");
    }

    [Fact]
    public void Validate_RejectsDuplicateCaseIds()
    {
        RepositoryHelpEvalCase evalCase = new(
            "duplicate",
            "architecture",
            "What does the repository contain?",
            "en",
            [],
            null,
            false,
            []);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => RepositoryHelpEvalDataset.Validate([evalCase, evalCase]));

        Assert.Contains("unique", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    internal static string GetDatasetPath() => Path.Combine(
        AppContext.BaseDirectory,
        "Datasets",
        "repository-help.v1.json");
}
