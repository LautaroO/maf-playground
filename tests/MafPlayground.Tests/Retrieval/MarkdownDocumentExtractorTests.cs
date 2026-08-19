using MafPlayground.Retrieval.Documents;

namespace MafPlayground.Tests.Retrieval;

public sealed class MarkdownDocumentExtractorTests
{
    [Fact]
    public async Task ExtractAsync_PreservesMarkdownSectionsAndHeaders()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"maf-ingestion-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(
            path,
            "# Account help\n\nReset from Settings.\n\n## Security\n\nUse MFA.");

        try
        {
            MarkdownDocumentExtractor extractor = new();

            ExtractedDocument document = await extractor.ExtractAsync(path);

            Assert.Equal(Path.GetFileNameWithoutExtension(path), document.Title);
            Assert.NotEmpty(document.Sections);
            Assert.Contains(document.Sections, section =>
                section.GetMarkdown().Contains("Settings", StringComparison.Ordinal));
            Assert.Contains(document.Sections, section =>
                section.GetMarkdown().Contains("Security", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
