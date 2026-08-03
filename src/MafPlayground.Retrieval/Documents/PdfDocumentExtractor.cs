using UglyToad.PdfPig;

namespace MafPlayground.Retrieval.Documents;

public sealed class PdfDocumentExtractor : IDocumentExtractor
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        List<ExtractedDocumentSection> sections = [];
        List<string> warnings = [];
        using PdfDocument document = PdfDocument.Open(path);
        foreach (UglyToad.PdfPig.Content.Page page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text = page.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                warnings.Add($"Page {page.Number} has no extractable text. OCR is not enabled.");
                continue;
            }
            sections.Add(new(text, page.Number, $"Page {page.Number}"));
        }

        if (sections.Count == 0)
        {
            warnings.Add("The PDF has no extractable text. It may be image-based and require OCR.");
        }

        return Task.FromResult(new ExtractedDocument(Path.GetFileNameWithoutExtension(path), sections, warnings));
    }
}
