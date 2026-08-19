using Microsoft.Extensions.DataIngestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace MafPlayground.Retrieval.Documents;

public sealed class PdfDocumentExtractor : IngestionDocumentReader, IDocumentExtractor
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public async Task<ExtractedDocument> ExtractAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileInfo source = new(path);
        IngestionDocument document = await ReadAsync(
            source,
            Path.GetFileNameWithoutExtension(path),
            "application/pdf",
            cancellationToken);

        int[] emptyPages = document.Sections
            .Where(section => section.Elements.Count == 0)
            .Select(section => section.PageNumber)
            .OfType<int>()
            .ToArray();
        IReadOnlyList<string> warnings = emptyPages.Length == 0
            ? []
            : emptyPages.Length == document.Sections.Count
                ? ["The PDF has no extractable text. It may be image-based and require OCR."]
                : emptyPages.Select(page =>
                    $"Page {page} has no extractable text. OCR is not enabled.").ToArray();

        return new ExtractedDocument(
            Path.GetFileNameWithoutExtension(path),
            document,
            warnings);
    }

    public override Task<IngestionDocument> ReadAsync(
        FileInfo source,
        string identifier,
        string? mediaType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        if (!source.Exists)
        {
            throw new FileNotFoundException("The specified file does not exist.", source.FullName);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using PdfDocument pdf = PdfDocument.Open(source.FullName);
        return Task.FromResult(ReadDocument(pdf, identifier, cancellationToken));
    }

    public override Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        cancellationToken.ThrowIfCancellationRequested();

        using PdfDocument pdf = PdfDocument.Open(source);
        return Task.FromResult(ReadDocument(pdf, identifier, cancellationToken));
    }

    private static IngestionDocument ReadDocument(
        PdfDocument pdf,
        string identifier,
        CancellationToken cancellationToken)
    {
        IngestionDocument document = new(identifier);
        foreach (UglyToad.PdfPig.Content.Page page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            IngestionDocumentSection section = new()
            {
                PageNumber = page.Number,
            };
            foreach (string paragraph in ExtractParagraphs(page))
            {
                section.Elements.Add(new IngestionDocumentParagraph(paragraph)
                {
                    PageNumber = page.Number,
                });
            }
            document.Sections.Add(section);
        }

        return document;
    }

    private static IReadOnlyList<string> ExtractParagraphs(
        UglyToad.PdfPig.Content.Page page)
    {
        IReadOnlyList<TextBlock> blocks = DocstrumBoundingBoxes.Instance.GetBlocks(
            NearestNeighbourWordExtractor.Instance.GetWords(page.Letters));
        IReadOnlyList<string> paragraphs = UnsupervisedReadingOrderDetector.Instance
            .Get(blocks)
            .Select(block => block.Text.Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        if (paragraphs.Count > 0)
        {
            return paragraphs;
        }

        return SplitParagraphs(ContentOrderTextExtractor.GetText(page).Trim())
            .ToArray();
    }

    private static IEnumerable<string> SplitParagraphs(string text) =>
        text.Split(
                ["\r\n\r\n", "\n\n", "\r\r"],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph));
}
