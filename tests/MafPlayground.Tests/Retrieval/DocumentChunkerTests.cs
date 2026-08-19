using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.DataIngestion;

namespace MafPlayground.Tests.Retrieval;

public sealed class DocumentChunkerTests
{
    [Fact]
    public void Chunk_PreservesSourceSectionPage()
    {
        DocumentChunker chunker = new();
        IngestionDocument ingestionDocument = new("Help");
        IngestionDocumentSection section = new() { PageNumber = 7 };
        section.Elements.Add(new IngestionDocumentParagraph(
            "First sentence. Second sentence.")
        {
            PageNumber = 7,
        });
        ingestionDocument.Sections.Add(section);
        ExtractedDocument document = new("Help", ingestionDocument, []);

        IReadOnlyList<DocumentChunk> chunks = chunker.Chunk(
            document,
            new KnowledgeIngestionSettings(
                ChunkSizeCharacters: 20,
                ChunkOverlapCharacters: 5,
                EmbeddingBatchSize: 1));

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, chunk => Assert.Equal(7, chunk.PageNumber));
        Assert.All(chunks, chunk => Assert.Equal("Page 7", chunk.SectionName));
    }
}
