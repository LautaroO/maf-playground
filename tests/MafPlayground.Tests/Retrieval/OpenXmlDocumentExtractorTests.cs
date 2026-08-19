using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.DataIngestion;
using Microsoft.ML.Tokenizers;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MafPlayground.Tests.Retrieval;

public sealed class OpenXmlDocumentExtractorTests
{
    [Fact]
    public async Task Docx_ExtractsHeadingsParagraphsListsAndTables()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"maf-ingestion-{Guid.NewGuid():N}.docx");
        CreateDocx(path);

        try
        {
            DocxDocumentExtractor extractor = new();

            ExtractedDocument document = await extractor.ExtractAsync(path);

            Assert.IsAssignableFrom<IngestionDocumentReader>(extractor);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), document.Title);
            Assert.Empty(document.Warnings);
            Assert.Equal(2, document.Sections.Count);
            Assert.Contains(
                document.Sections[0].Elements,
                element => element is IngestionDocumentHeader header &&
                           header.GetMarkdown().Contains("Security", StringComparison.Ordinal));
            Assert.Contains(
                document.Sections[0].Elements,
                element => element is IngestionDocumentParagraph paragraph &&
                           paragraph.GetMarkdown().Contains("MFA", StringComparison.Ordinal));
            Assert.Contains(
                document.Sections[0].Elements,
                element => element is IngestionDocumentParagraph paragraph &&
                           paragraph.GetMarkdown().StartsWith("- ", StringComparison.Ordinal));
            Assert.Contains(
                document.Sections[1].Elements,
                element => element is IngestionDocumentTable table &&
                           table.GetMarkdown().Contains("Active", StringComparison.Ordinal));

            IReadOnlyList<DocumentChunk> chunks = await
                new MicrosoftDataIngestionDocumentChunker().ChunkAsync(
                    document,
                    CreateChunkingSettings(),
                    CreateTokenizer());
            Assert.Contains(chunks, chunk => chunk.SectionName == "Security");
            Assert.Contains(chunks, chunk => chunk.SectionName == "Systems");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Pptx_ExtractsOneSectionPerSlideWithTitleAndBody()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"maf-ingestion-{Guid.NewGuid():N}.pptx");
        CreatePptx(path);

        try
        {
            PptxDocumentExtractor extractor = new();

            ExtractedDocument document = await extractor.ExtractAsync(path);

            Assert.IsAssignableFrom<IngestionDocumentReader>(extractor);
            Assert.Empty(document.Warnings);
            Assert.Equal([1, 2], document.Sections.Select(section => section.PageNumber));
            Assert.Contains("Architecture", document.Sections[0].GetMarkdown());
            Assert.Contains("provider neutral", document.Sections[0].GetMarkdown());
            Assert.Contains("Operations", document.Sections[1].GetMarkdown());
            Assert.Contains("bounded retries", document.Sections[1].GetMarkdown());

            IReadOnlyList<DocumentChunk> chunks = await
                new MicrosoftDataIngestionDocumentChunker().ChunkAsync(
                    document,
                    CreateChunkingSettings(),
                    CreateTokenizer());
            Assert.Contains(chunks, chunk =>
                chunk.PageNumber == 1 && chunk.SectionName == "Architecture");
            Assert.Contains(chunks, chunk =>
                chunk.PageNumber == 2 && chunk.SectionName == "Operations");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateDocx(string path)
    {
        using WordprocessingDocument package = WordprocessingDocument.Create(
            path,
            WordprocessingDocumentType.Document);
        MainDocumentPart mainPart = package.AddMainDocumentPart();
        StyleDefinitionsPart stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new W.Styles(
            CreateHeadingStyle("Heading1", "heading 1"));

        W.Paragraph securityHeading = CreateWordParagraph("Security");
        securityHeading.PrependChild(new W.ParagraphProperties(
            new W.ParagraphStyleId { Val = "Heading1" }));
        W.Paragraph listItem = CreateWordParagraph("Enable MFA");
        listItem.PrependChild(new W.ParagraphProperties(
            new W.NumberingProperties(
                new W.NumberingLevelReference { Val = 0 },
                new W.NumberingId { Val = 1 })));
        W.Paragraph systemsHeading = CreateWordParagraph("Systems");
        systemsHeading.PrependChild(new W.ParagraphProperties(
            new W.ParagraphStyleId { Val = "Heading1" }));

        W.Table table = new(
            new W.TableRow(
                new W.TableCell(CreateWordParagraph("System")),
                new W.TableCell(CreateWordParagraph("Status"))),
            new W.TableRow(
                new W.TableCell(CreateWordParagraph("RAG")),
                new W.TableCell(CreateWordParagraph("Active"))));
        mainPart.Document = new W.Document(new W.Body(
            securityHeading,
            CreateWordParagraph("Authentication requires MFA."),
            listItem,
            systemsHeading,
            table));
    }

    private static W.Style CreateHeadingStyle(string id, string name) =>
        new(new W.StyleName { Val = name })
        {
            Type = W.StyleValues.Paragraph,
            StyleId = id,
            CustomStyle = false,
        };

    private static W.Paragraph CreateWordParagraph(string text) =>
        new(new W.Run(new W.Text(text)));

    private static KnowledgeIngestionSettings CreateChunkingSettings() =>
        new(1)
        {
            MaxTokensPerChunk = 100,
            OverlapTokens = 10,
        };

    private static EmbeddingTokenizer CreateTokenizer() =>
        new LocalEmbeddingTokenizer(
            TiktokenTokenizer.CreateForEncoding("cl100k_base"),
            "test:cl100k_base");

    private static void CreatePptx(string path)
    {
        using PresentationDocument package = PresentationDocument.Create(
            path,
            PresentationDocumentType.Presentation);
        PresentationPart presentationPart = package.AddPresentationPart();
        P.SlideIdList slideIdList = new();
        presentationPart.Presentation = new P.Presentation(slideIdList);

        AddSlide(
            presentationPart,
            slideIdList,
            256U,
            "Architecture",
            "The design is provider neutral.");
        AddSlide(
            presentationPart,
            slideIdList,
            257U,
            "Operations",
            "Use bounded retries.");
    }

    private static void AddSlide(
        PresentationPart presentationPart,
        P.SlideIdList slideIdList,
        uint slideId,
        string title,
        string body)
    {
        SlidePart slidePart = presentationPart.AddNewPart<SlidePart>();
        slidePart.Slide = new P.Slide(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()),
                    CreatePowerPointShape(2U, "Title", title, isTitle: true),
                    CreatePowerPointShape(3U, "Body", body, isTitle: false))),
            new P.ColorMapOverride(new A.MasterColorMapping()));

        slideIdList.Append(new P.SlideId
        {
            Id = slideId,
            RelationshipId = presentationPart.GetIdOfPart(slidePart),
        });
    }

    private static P.Shape CreatePowerPointShape(
        uint id,
        string name,
        string text,
        bool isTitle)
    {
        P.ApplicationNonVisualDrawingProperties applicationProperties = new();
        if (isTitle)
        {
            applicationProperties.Append(new P.PlaceholderShape
            {
                Type = P.PlaceholderValues.Title,
            });
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks
                {
                    NoGrouping = true,
                }),
                applicationProperties),
            new P.ShapeProperties(),
            new P.TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(
                    new A.Run(new A.Text(text)),
                    new A.EndParagraphRunProperties())));
    }
}
