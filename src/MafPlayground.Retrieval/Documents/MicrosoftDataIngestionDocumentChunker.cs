using Microsoft.Extensions.DataIngestion;

namespace MafPlayground.Retrieval.Documents;

public sealed class MicrosoftDataIngestionDocumentChunker : IDocumentChunker
{
    private const string PackageVersion = "10.7.0-preview.1.26309.5";

    public async ValueTask<IReadOnlyList<DocumentChunk>> ChunkAsync(
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
            foreach (string chunk in await SplitAsync(
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

        return chunks;
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

    private static async ValueTask<IReadOnlyList<string>> SplitAsync(
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
            string remaining = content[start..];
            EmbeddingTokenBoundary end = await tokenizer.GetPrefixBoundaryAsync(
                remaining,
                options.MaxTokensPerChunk,
                cancellationToken);
            if (end.Index <= 0)
            {
                throw new InvalidOperationException(
                    $"Tokenizer '{tokenizer.Identity}' could not advance while " +
                    "splitting document content.");
            }

            string chunk = remaining[..end.Index];
            if (end.TokenCount > options.MaxTokensPerChunk)
            {
                throw new InvalidOperationException(
                    $"Tokenizer '{tokenizer.Identity}' produced a chunk with " +
                    $"{end.TokenCount} tokens, exceeding the configured maximum of " +
                    $"{options.MaxTokensPerChunk}.");
            }
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }
            if (end.Index == remaining.Length)
            {
                break;
            }

            EmbeddingTokenBoundary overlap = await tokenizer.GetSuffixBoundaryAsync(
                chunk,
                options.OverlapTokens,
                cancellationToken);
            int nextStart = start + end.Index - (chunk.Length - overlap.Index);
            start = nextStart > start ? nextStart : start + end.Index;
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
