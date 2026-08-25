using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.ML.Tokenizers;

namespace MafPlayground.Tests.Retrieval;

public sealed class MicrosoftDataIngestionDocumentChunkerTests
{
    [Fact]
    public async Task SectionChunker_UsesLocalTokenLimitAndPreservesSourceSection()
    {
        IngestionDocument ingestionDocument = new("Help");
        IngestionDocumentSection section = new() { PageNumber = 7 };
        section.Elements.Add(new IngestionDocumentParagraph(
            string.Join(' ', Enumerable.Repeat("retrieval evidence", 80)))
        {
            PageNumber = 7,
        });
        ingestionDocument.Sections.Add(section);
        KnowledgeIngestionSettings settings = new(1)
        {
            ChunkingStrategy = DocumentChunkingStrategy.Section,
            MaxTokensPerChunk = 24,
        };

        IReadOnlyList<DocumentChunk> chunks = await CreateChunker().ChunkAsync(
            new ExtractedDocument("Help", ingestionDocument, []),
            settings,
            new TopicEmbeddingGenerator());

        Tokenizer tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(
                tokenizer.CountTokens(chunk.Text, considerNormalization: false),
                1,
                24);
            Assert.Equal(7, chunk.PageNumber);
            Assert.Equal("Page 7", chunk.SectionName);
        });
        Assert.Contains(
            "section:tokenizer:cl100k_base:1.0.1:max:24:threshold:none",
            CreateChunker().GetIdentity(settings));
    }

    [Fact]
    public async Task HeaderChunker_PreservesHeadingContext()
    {
        IngestionDocument ingestionDocument = new("Help");
        IngestionDocumentSection section = new();
        section.Elements.Add(new IngestionDocumentHeader("# Reset account")
        {
            Level = 1,
        });
        section.Elements.Add(new IngestionDocumentParagraph(
            "Use the Settings page to reset the account."));
        ingestionDocument.Sections.Add(section);

        DocumentChunk chunk = Assert.Single(await CreateChunker().ChunkAsync(
            new ExtractedDocument("Help", ingestionDocument, []),
            new KnowledgeIngestionSettings(1)
            {
                ChunkingStrategy = DocumentChunkingStrategy.Header,
                MaxTokensPerChunk = 30,
            },
            new TopicEmbeddingGenerator()));

        Assert.Contains("Reset account", chunk.Text);
        Assert.Contains("Settings", chunk.Text);
        Assert.Equal("Reset account", chunk.SectionName);
    }

    [Fact]
    public async Task SemanticSimilarityChunker_SeparatesDissimilarAdjacentTopics()
    {
        IngestionDocument ingestionDocument = new("topics");
        IngestionDocumentSection section = new() { PageNumber = 3 };
        section.Elements.Add(new IngestionDocumentParagraph(
            ".NET is a cross-platform developer platform."));
        section.Elements.Add(new IngestionDocumentParagraph(
            "C# applications run on the .NET runtime."));
        section.Elements.Add(new IngestionDocumentParagraph(
            "Zeus is a deity from ancient Greek mythology."));
        ingestionDocument.Sections.Add(section);
        KnowledgeIngestionSettings settings = new(1)
        {
            ChunkingStrategy = DocumentChunkingStrategy.SemanticSimilarity,
            MaxTokensPerChunk = 100,
            SemanticSimilarityThresholdPercentile = 50,
        };

        IReadOnlyList<DocumentChunk> chunks = await CreateChunker().ChunkAsync(
            new ExtractedDocument("Topics", ingestionDocument, []),
            settings,
            new TopicEmbeddingGenerator());

        Assert.Collection(
            chunks,
            first =>
            {
                Assert.Contains(".NET", first.Text);
                Assert.Contains("C#", first.Text);
                Assert.DoesNotContain("Zeus", first.Text);
                Assert.Equal(3, first.PageNumber);
            },
            second =>
            {
                Assert.Equal(
                    "Zeus is a deity from ancient Greek mythology.",
                    second.Text);
                Assert.Equal(3, second.PageNumber);
            });
        Assert.Contains(
            "semanticsimilarity:tokenizer:cl100k_base:1.0.1:max:100:threshold:50",
            CreateChunker().GetIdentity(settings));
    }

    private static MicrosoftDataIngestionDocumentChunker CreateChunker() => new();

    private sealed class TopicEmbeddingGenerator
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(value => value.Contains("Zeus", StringComparison.Ordinal)
                    ? new Embedding<float>(new float[] { -1, 0 })
                    : new Embedding<float>(new float[] { 1, 0 }))));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
