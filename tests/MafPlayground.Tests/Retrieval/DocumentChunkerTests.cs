using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Documents;

namespace MafPlayground.Tests.Retrieval;

public sealed class DocumentChunkerTests
{
    [Fact]
    public void Chunk_PreservesSourceSectionPage()
    {
        DocumentChunker chunker = new();
        ExtractedDocument document = new("Help", [new("First sentence. Second sentence.", 7, "Page 7")], []);

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
