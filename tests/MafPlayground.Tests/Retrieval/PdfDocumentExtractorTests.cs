using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.DataIngestion;

namespace MafPlayground.Tests.Retrieval;

public sealed class PdfDocumentExtractorTests
{
    [Fact]
    public async Task ExtractAsync_SampleHelpPdfPreservesFourTextPages()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "help.pdf");
        PdfDocumentExtractor extractor = new();

        ExtractedDocument document = await extractor.ExtractAsync(path);

        Assert.Equal("help", document.Title);
        Assert.Equal([1, 2, 3, 4], document.Sections.Select(section => section.PageNumber));
        Assert.Empty(document.Warnings);
        Assert.Contains("30 minutes", document.Sections[1].GetMarkdown());
        Assert.Contains("02:00 UTC", document.Sections[3].GetMarkdown());
        Assert.IsAssignableFrom<IngestionDocumentReader>(extractor);
        Assert.All(
            document.Sections,
            section => Assert.All(
                section.Elements,
                element => Assert.Equal(section.PageNumber, element.PageNumber)));
    }
}
