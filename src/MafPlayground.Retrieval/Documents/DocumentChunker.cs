using Microsoft.Extensions.Options;

namespace MafPlayground.Retrieval.Documents;

public sealed class DocumentChunker(IOptions<RetrievalOptions> options)
{
    private readonly RetrievalOptions _options = options.Value;

    public IReadOnlyList<DocumentChunk> Chunk(ExtractedDocument document)
    {
        List<DocumentChunk> chunks = [];
        foreach (ExtractedDocumentSection section in document.Sections)
        {
            string text = NormalizeWhitespace(section.Text);
            int start = 0;
            while (start < text.Length)
            {
                int length = Math.Min(_options.ChunkSizeCharacters, text.Length - start);
                int end = FindBoundary(text, start, length);
                string content = text[start..end].Trim();
                if (content.Length > 0) chunks.Add(new(chunks.Count, content, section.PageNumber, section.Name));
                if (end >= text.Length) break;
                start = Math.Max(start + 1, end - _options.ChunkOverlapCharacters);
            }
        }
        return chunks;
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
}

public sealed record DocumentChunk(int Index, string Text, int? PageNumber, string? SectionName);
