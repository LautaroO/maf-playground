using Microsoft.Extensions.DataIngestion;

namespace MafPlayground.Retrieval.Documents;

public interface IDocumentExtractor
{
    IReadOnlySet<string> SupportedExtensions { get; }
    Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default);
}

public sealed record ExtractedDocument(
    string Title,
    IngestionDocument Document,
    IReadOnlyList<string> Warnings)
{
    public IList<IngestionDocumentSection> Sections => Document.Sections;
}

public sealed class DocumentExtractorRegistry
{
    private readonly IReadOnlyDictionary<string, IDocumentExtractor> _extractors;

    public DocumentExtractorRegistry(IEnumerable<IDocumentExtractor> extractors)
    {
        Dictionary<string, IDocumentExtractor> resolved = new(StringComparer.OrdinalIgnoreCase);
        foreach (IDocumentExtractor extractor in extractors)
        {
            foreach (string extension in extractor.SupportedExtensions)
            {
                string normalized = NormalizeExtension(extension);
                if (!resolved.TryAdd(normalized, extractor))
                {
                    throw new InvalidOperationException($"More than one document extractor handles '{normalized}'.");
                }
            }
        }
        _extractors = resolved;
    }

    public IReadOnlyCollection<string> SupportedExtensions => _extractors.Keys.ToArray();

    public IDocumentExtractor Resolve(string path)
    {
        string extension = NormalizeExtension(Path.GetExtension(path));
        return _extractors.TryGetValue(extension, out IDocumentExtractor? extractor)
            ? extractor
            : throw new NotSupportedException($"No document extractor is registered for '{extension}'.");
    }

    private static string NormalizeExtension(string extension) =>
        extension.StartsWith('.') ? extension : $".{extension}";
}
