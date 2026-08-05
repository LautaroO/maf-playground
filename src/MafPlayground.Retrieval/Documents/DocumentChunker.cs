namespace MafPlayground.Retrieval.Documents;

public sealed class DocumentChunker
{
    public IReadOnlyList<DocumentChunk> Chunk(
        ExtractedDocument document,
        KnowledgeIngestionSettings options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        List<DocumentChunk> chunks = [];
        foreach (ExtractedDocumentSection section in document.Sections)
        {
            string text = NormalizeWhitespace(section.Text);
            int start = 0;
            while (start < text.Length)
            {
                int length = Math.Min(options.ChunkSizeCharacters, text.Length - start);
                int end = FindBoundary(text, start, length);
                string content = text[start..end].Trim();
                if (content.Length > 0) chunks.Add(new(chunks.Count, content, section.PageNumber, section.Name));
                if (end >= text.Length) break;
                start = Math.Max(start + 1, end - options.ChunkOverlapCharacters);
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
