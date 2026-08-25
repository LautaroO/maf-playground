using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.ML.Tokenizers;

namespace MafPlayground.Retrieval.Documents;

public sealed class MicrosoftDataIngestionDocumentChunker : IDocumentChunker
{
    private const string ChunkingTokenizerIdentity = "cl100k_base:1.0.1";
    private const string PackageVersion = "10.7.0-preview.1.26309.5";
    private readonly Tokenizer _tokenizer;

    public MicrosoftDataIngestionDocumentChunker()
        : this(TiktokenTokenizer.CreateForEncoding("cl100k_base"))
    {
    }

    internal MicrosoftDataIngestionDocumentChunker(Tokenizer tokenizer)
    {
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
    }

    public async ValueTask<IReadOnlyList<DocumentChunk>> ChunkAsync(
        ExtractedDocument document,
        KnowledgeIngestionSettings options,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);

        List<DocumentChunk> chunks = [];
        foreach (IngestionDocumentSection sourceSection in document.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IngestionDocument sectionDocument = new(document.Document.Identifier);
            sectionDocument.Sections.Add(sourceSection);
            IngestionChunker<string> chunker = CreateChunker(
                options,
                embeddingGenerator);

            await foreach (IngestionChunk<string> chunk in chunker.ProcessAsync(
                               sectionDocument,
                               cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(chunk.Content))
                {
                    chunks.Add(new DocumentChunk(
                        chunks.Count,
                        chunk.Content,
                        sourceSection.PageNumber,
                        FindSectionName(sourceSection)));
                }
            }
        }

        return chunks;
    }

    public string GetIdentity(KnowledgeIngestionSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string strategy = options.ChunkingStrategy.ToString().ToLowerInvariant();
        string threshold = options.ChunkingStrategy ==
            DocumentChunkingStrategy.SemanticSimilarity
                ? options.SemanticSimilarityThresholdPercentile.ToString(
                    CultureInfo.InvariantCulture)
                : "none";
        return $"data-ingestion:{PackageVersion}:{strategy}:" +
               $"tokenizer:{ChunkingTokenizerIdentity}:" +
               $"max:{options.MaxTokensPerChunk}:threshold:{threshold}";
    }

    private IngestionChunker<string> CreateChunker(
        KnowledgeIngestionSettings options,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        IngestionChunkerOptions chunkerOptions = new(_tokenizer)
        {
            MaxTokensPerChunk = options.MaxTokensPerChunk,
            // The selected Microsoft element chunkers do not apply overlap.
            OverlapTokens = 0,
        };
        return options.ChunkingStrategy switch
        {
            DocumentChunkingStrategy.Section => new SectionChunker(chunkerOptions),
            DocumentChunkingStrategy.Header => new HeaderChunker(chunkerOptions),
            DocumentChunkingStrategy.SemanticSimilarity =>
                new SemanticSimilarityChunker(
                    embeddingGenerator,
                    chunkerOptions,
                    options.SemanticSimilarityThresholdPercentile),
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ChunkingStrategy,
                "Unsupported document chunking strategy."),
        };
    }

    private static string? FindSectionName(IngestionDocumentSection section) =>
        section.Elements
            .OfType<IngestionDocumentHeader>()
            .Select(header => header.GetMarkdown().Trim().TrimStart('#').Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
        (section.PageNumber is int pageNumber ? $"Page {pageNumber}" : null);
}
