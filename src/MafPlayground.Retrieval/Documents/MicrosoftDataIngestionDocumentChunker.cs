using Microsoft.Extensions.DataIngestion;

namespace MafPlayground.Retrieval.Documents;

public sealed class MicrosoftDataIngestionDocumentChunker : IDocumentChunker
{
    private const string PackageVersion = "10.7.0-preview.1.26309.5";

    public ValueTask<IReadOnlyList<DocumentChunk>> ChunkAsync(
        ExtractedDocument document,
        KnowledgeIngestionSettings options,
        EmbeddingTokenizer tokenizer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenizer);

        List<DocumentChunk> chunks = [];
        foreach (IngestionDocumentSection sourceSection in document.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? sectionName = FindSectionName(sourceSection);
            string content = sourceSection.GetMarkdown();
            foreach (string chunk in Split(
                         content,
                         options,
                         tokenizer,
                         cancellationToken))
            {
                chunks.Add(new DocumentChunk(
                    chunks.Count,
                    chunk,
                    sourceSection.PageNumber,
                    sectionName));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<DocumentChunk>>(chunks);
    }

    public string GetIdentity(
        KnowledgeIngestionSettings options,
        EmbeddingTokenizer tokenizer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenizer);
        return $"data-ingestion:{PackageVersion}:document-tokens-per-section:" +
               $"{tokenizer.Identity}:max:{options.MaxTokensPerChunk}:" +
               $"overlap:{options.OverlapTokens}";
    }

    private static IReadOnlyList<string> Split(
        string content,
        KnowledgeIngestionSettings options,
        EmbeddingTokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        List<string> chunks = [];
        if (string.IsNullOrWhiteSpace(content))
        {
            return chunks;
        }

        int start = 0;
        while (start < content.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<char> remaining = content.AsSpan(start);
            int endOffset = tokenizer.Instance.GetIndexByTokenCount(
                remaining,
                options.MaxTokensPerChunk,
                out string? _,
                out int _,
                considerNormalization: true);
            if (endOffset <= 0)
            {
                throw new InvalidOperationException(
                    $"Tokenizer '{tokenizer.Identity}' could not advance while " +
                    "splitting document content.");
            }

            string chunk = remaining[..endOffset].ToString();
            int tokenCount = tokenizer.Instance.CountTokens(
                chunk,
                considerNormalization: true);
            if (tokenCount > options.MaxTokensPerChunk)
            {
                throw new InvalidOperationException(
                    $"Tokenizer '{tokenizer.Identity}' produced a chunk with " +
                    $"{tokenCount} tokens, exceeding the configured maximum of " +
                    $"{options.MaxTokensPerChunk}.");
            }
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }
            if (endOffset == remaining.Length)
            {
                break;
            }

            int overlapStart = tokenizer.Instance.GetIndexByTokenCountFromEnd(
                chunk,
                options.OverlapTokens,
                out string? _,
                out int _,
                considerNormalization: true);
            int nextStart = start + endOffset - (chunk.Length - overlapStart);
            start = nextStart > start ? nextStart : start + endOffset;
        }

        return chunks;
    }

    private static string? FindSectionName(IngestionDocumentSection section) =>
        section.Elements
            .OfType<IngestionDocumentHeader>()
            .Select(header => header.GetMarkdown().Trim().TrimStart('#').Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
        (section.PageNumber is int pageNumber ? $"Page {pageNumber}" : null);
}
