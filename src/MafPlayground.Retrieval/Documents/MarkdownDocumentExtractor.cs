using Microsoft.Extensions.DataIngestion;

namespace MafPlayground.Retrieval.Documents;

public sealed class MarkdownDocumentExtractor : IDocumentExtractor
{
    private static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" };
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public async Task<ExtractedDocument> ExtractAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileInfo file = new(path);
        IngestionDocumentReader reader = new MarkdownReader();
        IngestionDocument document = await reader.ReadAsync(
            file,
            cancellationToken);

        return new ExtractedDocument(
            Path.GetFileNameWithoutExtension(path),
            document,
            []);
    }
}
