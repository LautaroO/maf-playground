using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.DataIngestion;
using Microsoft.ML.Tokenizers;

namespace MafPlayground.Tests.Retrieval;

public sealed class MicrosoftDataIngestionDocumentChunkerTests
{
    [Fact]
    public async Task ChunkAsync_UsesTokenLimitAndPreservesSourceSection()
    {
        MicrosoftDataIngestionDocumentChunker chunker = new();
        IngestionDocument ingestionDocument = new("Help");
        IngestionDocumentSection section = new() { PageNumber = 7 };
        section.Elements.Add(new IngestionDocumentParagraph(
            string.Join(' ', Enumerable.Repeat("retrieval evidence", 80)))
        {
            PageNumber = 7,
        });
        ingestionDocument.Sections.Add(section);
        ExtractedDocument document = new(
            "Help",
            ingestionDocument,
            []);
        KnowledgeIngestionSettings settings = new(1)
        {
            MaxTokensPerChunk = 24,
            OverlapTokens = 4,
        };
        EmbeddingTokenizer tokenizer = CreateTokenizer();

        IReadOnlyList<DocumentChunk> chunks = await chunker.ChunkAsync(
            document,
            settings,
            tokenizer);

        Tokenizer actualTokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(
                actualTokenizer.CountTokens(chunk.Text, considerNormalization: false),
                1,
                24);
            Assert.Equal(7, chunk.PageNumber);
            Assert.Equal("Page 7", chunk.SectionName);
        });
        Assert.Contains(
            "document-tokens-per-section:test:cl100k_base:max:24:overlap:4",
            chunker.GetIdentity(settings, tokenizer));
    }

    [Fact]
    public async Task ChunkAsync_PreservesMarkdownHeaderContext()
    {
        MicrosoftDataIngestionDocumentChunker chunker = new();
        IngestionDocument ingestionDocument = new("Help");
        IngestionDocumentSection section = new();
        section.Elements.Add(new IngestionDocumentHeader("# Reset account")
        {
            Level = 1,
        });
        section.Elements.Add(new IngestionDocumentParagraph(
            "Use the Settings page to reset the account."));
        ingestionDocument.Sections.Add(section);
        ExtractedDocument document = new(
            "Help",
            ingestionDocument,
            []);
        KnowledgeIngestionSettings settings = new(1)
        {
            MaxTokensPerChunk = 30,
            OverlapTokens = 0,
        };

        DocumentChunk chunk = Assert.Single(await chunker.ChunkAsync(
            document,
            settings,
            CreateTokenizer()));

        Assert.Contains("Reset account", chunk.Text);
        Assert.Contains("Settings", chunk.Text);
    }

    private static EmbeddingTokenizer CreateTokenizer() =>
        new(
            TiktokenTokenizer.CreateForEncoding("cl100k_base"),
            "test:cl100k_base");
}
