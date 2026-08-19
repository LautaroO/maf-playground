using Microsoft.Extensions.DataIngestion;

namespace MafPlayground.Retrieval.Documents;

public interface IDocumentChunker
{
    ValueTask<IReadOnlyList<DocumentChunk>> ChunkAsync(
        ExtractedDocument document,
        KnowledgeIngestionSettings options,
        CancellationToken cancellationToken = default);

    string GetIdentity(KnowledgeIngestionSettings options);
}

public sealed class DocumentChunker : IDocumentChunker
{
    public IReadOnlyList<DocumentChunk> Chunk(
        ExtractedDocument document,
        KnowledgeIngestionSettings options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        List<DocumentChunk> chunks = [];
        foreach (IngestionDocumentSection section in document.Sections)
        {
            string text = NormalizeWhitespace(section.GetMarkdown());
            string? sectionName = FindSectionName(section);
            int start = 0;
            while (start < text.Length)
            {
                int length = Math.Min(options.ChunkSizeCharacters, text.Length - start);
                int end = FindBoundary(text, start, length);
                string content = text[start..end].Trim();
                if (content.Length > 0)
                {
                    chunks.Add(new(
                        chunks.Count,
                        content,
                        section.PageNumber,
                        sectionName));
                }
                if (end >= text.Length) break;
                start = Math.Max(start + 1, end - options.ChunkOverlapCharacters);
            }
        }
        return chunks;
    }

    public ValueTask<IReadOnlyList<DocumentChunk>> ChunkAsync(
        ExtractedDocument document,
        KnowledgeIngestionSettings options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Chunk(document, options));
    }

    public string GetIdentity(KnowledgeIngestionSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return $"chars:{options.ChunkSizeCharacters}:overlap:{options.ChunkOverlapCharacters}";
    }

    private static int FindBoundary(string text, int start, int length)
    {
        int proposed = start + length;
        if (proposed >= text.Length) return text.Length;
        int boundary = text.LastIndexOfAny(['\n', '.', '!', '?', ' '], proposed - 1, length);
        return boundary > start + (length / 2) ? boundary + 1 : proposed;
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? FindSectionName(IngestionDocumentSection section) =>
        section.Elements
            .OfType<IngestionDocumentHeader>()
            .Select(header => header.Text?.Trim().TrimStart('#').Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
        (section.PageNumber is int pageNumber ? $"Page {pageNumber}" : null);
}

public sealed record DocumentChunk(int Index, string Text, int? PageNumber, string? SectionName);
