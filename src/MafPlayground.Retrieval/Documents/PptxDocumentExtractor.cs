using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DataIngestion;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace MafPlayground.Retrieval.Documents;

public sealed class PptxDocumentExtractor : IngestionDocumentReader, IDocumentExtractor
{
    private static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pptx" };

    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public Task<ExtractedDocument> ExtractAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileInfo source = new(path);
        OpenXmlExtractionResult result = ReadFile(
            source,
            Path.GetFileNameWithoutExtension(path),
            cancellationToken);
        return Task.FromResult(new ExtractedDocument(
            Path.GetFileNameWithoutExtension(path),
            result.Document,
            result.Warnings));
    }

    public override Task<IngestionDocument> ReadAsync(
        FileInfo source,
        string identifier,
        string? mediaType = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ReadFile(source, identifier, cancellationToken).Document);

    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        using MemoryStream copy = new();
        await source.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        using PresentationDocument package = PresentationDocument.Open(copy, false);
        return ReadPackage(package, identifier, cancellationToken).Document;
    }

    private static OpenXmlExtractionResult ReadFile(
        FileInfo source,
        string identifier,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        if (!source.Exists)
        {
            throw new FileNotFoundException(
                "The specified file does not exist.",
                source.FullName);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using PresentationDocument package = PresentationDocument.Open(
            source.FullName,
            false);
        return ReadPackage(package, identifier, cancellationToken);
    }

    private static OpenXmlExtractionResult ReadPackage(
        PresentationDocument package,
        string identifier,
        CancellationToken cancellationToken)
    {
        IngestionDocument document = new(identifier);
        List<string> warnings = [];
        PresentationPart? presentationPart = package.PresentationPart;
        P.SlideIdList? slideIds = presentationPart?.Presentation?.SlideIdList;
        if (presentationPart is null || slideIds is null)
        {
            warnings.Add("The PPTX presentation has no readable slide list.");
            return new(document, warnings);
        }

        int slideNumber = 0;
        foreach (P.SlideId slideId in slideIds.Elements<P.SlideId>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            slideNumber++;
            string? relationshipId = slideId.RelationshipId?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId) ||
                presentationPart.GetPartById(relationshipId) is not SlidePart slidePart)
            {
                warnings.Add($"Slide {slideNumber} could not be resolved.");
                continue;
            }

            IngestionDocumentSection section = ReadSlide(slidePart, slideNumber);
            document.Sections.Add(section);
            AddUnsupportedContentWarnings(slidePart, slideNumber, warnings);
        }

        if (document.Sections.Count == 0)
        {
            warnings.Add("The PPTX presentation has no readable slides.");
        }
        return new(document, warnings);
    }

    private static IngestionDocumentSection ReadSlide(
        SlidePart slidePart,
        int slideNumber)
    {
        IngestionDocumentSection section = new()
        {
            PageNumber = slideNumber,
        };
        P.ShapeTree? shapeTree = slidePart.Slide?.CommonSlideData?.ShapeTree;
        if (shapeTree is null)
        {
            section.Elements.Add(CreateHeader($"Slide {slideNumber}", slideNumber));
            return section;
        }

        List<(P.Shape Shape, IReadOnlyList<string> Paragraphs)> shapes = shapeTree
            .Descendants<P.Shape>()
            .Select(shape => (Shape: shape, Paragraphs: GetShapeParagraphs(shape)))
            .Where(item => item.Paragraphs.Count > 0)
            .ToList();
        (P.Shape Shape, IReadOnlyList<string> Paragraphs) titleShape = shapes
            .FirstOrDefault(item => IsTitleShape(item.Shape));
        bool hasTitle = titleShape.Shape is not null;
        string title = hasTitle
            ? titleShape.Paragraphs[0]
            : $"Slide {slideNumber}";
        section.Elements.Add(CreateHeader(title, slideNumber));

        foreach ((P.Shape shape, IReadOnlyList<string> paragraphs) in shapes)
        {
            bool isTitle = hasTitle && ReferenceEquals(shape, titleShape.Shape);
            for (int index = isTitle ? 1 : 0; index < paragraphs.Count; index++)
            {
                section.Elements.Add(new IngestionDocumentParagraph(
                    paragraphs[index])
                {
                    PageNumber = slideNumber,
                });
            }
        }

        foreach (A.Table table in shapeTree.Descendants<A.Table>())
        {
            IngestionDocumentTable? ingestionTable = ConvertTable(
                table,
                slideNumber);
            if (ingestionTable is not null)
            {
                section.Elements.Add(ingestionTable);
            }
        }
        return section;
    }

    private static IngestionDocumentHeader CreateHeader(
        string text,
        int slideNumber) =>
        new($"# {text}")
        {
            Level = 1,
            PageNumber = slideNumber,
        };

    private static IReadOnlyList<string> GetShapeParagraphs(P.Shape shape) =>
        shape.TextBody?
            .Elements<A.Paragraph>()
            .Select(GetParagraphText)
            .Where(text => text.Length > 0)
            .ToArray() ?? [];

    private static string GetParagraphText(A.Paragraph paragraph) =>
        NormalizeText(string.Concat(
            paragraph.Descendants<A.Text>().Select(text => text.Text)));

    private static bool IsTitleShape(P.Shape shape)
    {
        P.PlaceholderShape? placeholder = shape.NonVisualShapeProperties?
            .ApplicationNonVisualDrawingProperties?
            .GetFirstChild<P.PlaceholderShape>();
        P.PlaceholderValues? type = placeholder?.Type?.Value;
        return type == P.PlaceholderValues.Title ||
            type == P.PlaceholderValues.CenteredTitle;
    }

    private static IngestionDocumentTable? ConvertTable(
        A.Table table,
        int slideNumber)
    {
        A.TableRow[] rows = table.Elements<A.TableRow>().ToArray();
        int columnCount = rows
            .Select(row => row.Elements<A.TableCell>().Count())
            .DefaultIfEmpty()
            .Max();
        if (rows.Length == 0 || columnCount == 0)
        {
            return null;
        }

        string[,] values = new string[rows.Length, columnCount];
        IngestionDocumentElement[,] cells =
            new IngestionDocumentElement[rows.Length, columnCount];
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            A.TableCell[] rowCells = rows[rowIndex]
                .Elements<A.TableCell>()
                .ToArray();
            for (int columnIndex = 0; columnIndex < rowCells.Length; columnIndex++)
            {
                string value = NormalizeText(string.Join(
                    ' ',
                    rowCells[columnIndex]
                        .Descendants<A.Paragraph>()
                        .Select(GetParagraphText)));
                values[rowIndex, columnIndex] = value;
                if (value.Length > 0)
                {
                    cells[rowIndex, columnIndex] =
                        new IngestionDocumentParagraph(value)
                        {
                            PageNumber = slideNumber,
                        };
                }
            }
        }

        return new IngestionDocumentTable(BuildMarkdownTable(values), cells)
        {
            PageNumber = slideNumber,
        };
    }

    private static string BuildMarkdownTable(string[,] values)
    {
        int rows = values.GetLength(0);
        int columns = values.GetLength(1);
        List<string> lines = [];
        lines.Add(BuildMarkdownRow(values, 0, columns));
        lines.Add($"| {string.Join(" | ", Enumerable.Repeat("---", columns))} |");
        for (int row = 1; row < rows; row++)
        {
            lines.Add(BuildMarkdownRow(values, row, columns));
        }
        return string.Join('\n', lines);
    }

    private static string BuildMarkdownRow(
        string[,] values,
        int row,
        int columns) =>
        $"| {string.Join(" | ", Enumerable.Range(0, columns)
            .Select(column => values[row, column]
                .Replace("|", "\\|", StringComparison.Ordinal)))} |";

    private static void AddUnsupportedContentWarnings(
        SlidePart slidePart,
        int slideNumber,
        ICollection<string> warnings)
    {
        if (slidePart.ImageParts.Any())
        {
            warnings.Add(
                $"Slide {slideNumber} contains images; image content was not extracted.");
        }
        if (slidePart.ChartParts.Any())
        {
            warnings.Add(
                $"Slide {slideNumber} contains charts; chart content was not extracted.");
        }
        if (slidePart.NotesSlidePart?.NotesSlide?.Descendants<A.Text>()
            .Any(text => !string.IsNullOrWhiteSpace(text.Text)) == true)
        {
            warnings.Add(
                $"Slide {slideNumber} contains speaker notes; notes were not extracted.");
        }
    }

    private static string NormalizeText(string? value) =>
        string.Join(
            ' ',
            (value ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
}
