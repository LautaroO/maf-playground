using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.ML.Tokenizers;

namespace MafPlayground.Retrieval.Documents;

public sealed class MicrosoftDataIngestionDocumentChunker : IDocumentChunker
{
    private const string PackageVersion = "10.7.0-preview.1.26309.5";

    public async ValueTask<IReadOnlyList<DocumentChunk>> ChunkAsync(
        ExtractedDocument document,
        KnowledgeIngestionSettings options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        Tokenizer tokenizer = TiktokenTokenizer.CreateForEncoding(
            options.TokenizerEncoding);
        DocumentTokenChunker chunker = new(new IngestionChunkerOptions(tokenizer)
        {
            MaxTokensPerChunk = options.MaxTokensPerChunk,
            OverlapTokens = options.OverlapTokens,
        });

        List<DocumentChunk> chunks = [];
        foreach (IngestionDocumentSection sourceSection in document.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IngestionDocument sectionDocument = new(document.Document.Identifier);
            sectionDocument.Sections.Add(sourceSection);
            string? sectionName = FindSectionName(sourceSection);

            await foreach (IngestionChunk<string> chunk in chunker.ProcessAsync(
                               sectionDocument,
                               cancellationToken))
            {
                string content = chunk.Content;
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                chunks.Add(new DocumentChunk(
                    chunks.Count,
                    content,
                    sourceSection.PageNumber,
                    sectionName ?? NormalizeContext(chunk.Context)));
            }
        }

        return chunks;
    }

    public string GetIdentity(KnowledgeIngestionSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return $"data-ingestion:{PackageVersion}:document-tokens-per-section:" +
               $"{options.TokenizerEncoding}:max:{options.MaxTokensPerChunk}:" +
               $"overlap:{options.OverlapTokens}";
    }

    private static string? NormalizeContext(string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return null;
        }

        string value = context.Trim().TrimStart('#').Trim();
        return value.Length == 0 ? null : value;
    }

    private static string? FindSectionName(IngestionDocumentSection section) =>
        section.Elements
            .OfType<IngestionDocumentHeader>()
            .Select(header => header.Text?.Trim().TrimStart('#').Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
        (section.PageNumber is int pageNumber ? $"Page {pageNumber}" : null);
}
