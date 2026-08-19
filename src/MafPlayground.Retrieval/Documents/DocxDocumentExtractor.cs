using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DataIngestion;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MafPlayground.Retrieval.Documents;

public sealed class DocxDocumentExtractor : IngestionDocumentReader, IDocumentExtractor
{
    private static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx" };

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
        using WordprocessingDocument package = WordprocessingDocument.Open(copy, false);
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
        using WordprocessingDocument package = WordprocessingDocument.Open(
            source.FullName,
            false);
        return ReadPackage(package, identifier, cancellationToken);
    }

    private static OpenXmlExtractionResult ReadPackage(
        WordprocessingDocument package,
        string identifier,
        CancellationToken cancellationToken)
    {
        IngestionDocument document = new(identifier);
        List<string> warnings = [];
        MainDocumentPart? mainPart = package.MainDocumentPart;
        W.Body? body = mainPart?.Document?.Body;
        if (mainPart is null || body is null)
        {
            warnings.Add("The DOCX document has no readable main document body.");
            return new(document, warnings);
        }

        IReadOnlyDictionary<string, int> headingStyles = GetHeadingStyles(mainPart);
        IngestionDocumentSection section = new();
        foreach (OpenXmlElement element in body.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (element)
            {
                case W.Paragraph paragraph:
                    AddParagraph(document, ref section, paragraph, headingStyles);
                    break;
                case W.Table table:
                    IngestionDocumentTable? ingestionTable = ConvertTable(table);
                    if (ingestionTable is not null)
                    {
                        section.Elements.Add(ingestionTable);
                    }
                    break;
            }
        }
        AddSectionIfNotEmpty(document, section);

        if (document.Sections.Count == 0)
        {
            warnings.Add("The DOCX document has no extractable text or tables.");
        }
        if (mainPart.ImageParts.Any())
        {
            warnings.Add("The DOCX document contains images; image content was not extracted.");
        }

        return new(document, warnings);
    }

    private static void AddParagraph(
        IngestionDocument document,
        ref IngestionDocumentSection section,
        W.Paragraph paragraph,
        IReadOnlyDictionary<string, int> headingStyles)
    {
        string text = NormalizeText(paragraph.InnerText);
        if (text.Length == 0)
        {
            return;
        }

        int? headingLevel = GetHeadingLevel(paragraph, headingStyles);
        if (headingLevel is int level)
        {
            AddSectionIfNotEmpty(document, section);
            section = new IngestionDocumentSection();
            section.Elements.Add(new IngestionDocumentHeader(
                $"{new string('#', level)} {text}")
            {
                Level = level,
            });
            return;
        }

        bool isListItem = paragraph.ParagraphProperties?
            .NumberingProperties is not null;
        section.Elements.Add(new IngestionDocumentParagraph(
            isListItem ? $"- {text}" : text));
    }

    private static IReadOnlyDictionary<string, int> GetHeadingStyles(
        MainDocumentPart mainPart)
    {
        Dictionary<string, int> result = new(StringComparer.OrdinalIgnoreCase);
        W.Styles? styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles is null)
        {
            return result;
        }

        foreach (W.Style style in styles.Elements<W.Style>())
        {
            string? styleId = style.StyleId?.Value;
            string? styleName = style.StyleName?.Val?.Value;
            int? level = ParseHeadingLevel(styleId) ?? ParseHeadingLevel(styleName);
            if (!string.IsNullOrWhiteSpace(styleId) && level is not null)
            {
                result[styleId] = level.Value;
            }
        }
        return result;
    }

    private static int? GetHeadingLevel(
        W.Paragraph paragraph,
        IReadOnlyDictionary<string, int> headingStyles)
    {
        string? styleId = paragraph.ParagraphProperties?
            .ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }
        if (headingStyles.TryGetValue(styleId, out int level))
        {
            return level;
        }
        return ParseHeadingLevel(styleId);
    }

    private static int? ParseHeadingLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = new(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        const string HeadingPrefix = "heading";
        if (!normalized.StartsWith(HeadingPrefix, StringComparison.Ordinal))
        {
            return null;
        }
        return int.TryParse(normalized[HeadingPrefix.Length..], out int level)
            ? Math.Clamp(level, 1, 6)
            : null;
    }

    private static IngestionDocumentTable? ConvertTable(W.Table table)
    {
        W.TableRow[] rows = table.Elements<W.TableRow>().ToArray();
        int columnCount = rows
            .Select(row => row.Elements<W.TableCell>().Count())
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
            W.TableCell[] rowCells = rows[rowIndex]
                .Elements<W.TableCell>()
                .ToArray();
            for (int columnIndex = 0; columnIndex < rowCells.Length; columnIndex++)
            {
                string value = NormalizeText(string.Join(
                    ' ',
                    rowCells[columnIndex]
                        .Elements<W.Paragraph>()
                        .Select(paragraph => paragraph.InnerText)));
                values[rowIndex, columnIndex] = value;
                if (value.Length > 0)
                {
                    cells[rowIndex, columnIndex] =
                        new IngestionDocumentParagraph(value);
                }
            }
        }

        return new IngestionDocumentTable(BuildMarkdownTable(values), cells);
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
            .Select(column => EscapeMarkdownCell(values[row, column])))} |";

    private static string EscapeMarkdownCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string NormalizeText(string? value) =>
        string.Join(
            ' ',
            (value ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

    private static void AddSectionIfNotEmpty(
        IngestionDocument document,
        IngestionDocumentSection section)
    {
        if (section.Elements.Count > 0)
        {
            document.Sections.Add(section);
        }
    }
}
