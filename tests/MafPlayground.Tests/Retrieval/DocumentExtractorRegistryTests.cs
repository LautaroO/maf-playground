using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.DataIngestion;

namespace MafPlayground.Tests.Retrieval;

public sealed class DocumentExtractorRegistryTests
{
    [Fact]
    public void Resolve_UsesExtensionWithoutDependingOnConcreteFormat()
    {
        FakeExtractor extractor = new(".md");
        DocumentExtractorRegistry registry = new([extractor]);

        Assert.Same(extractor, registry.Resolve("HELP.MD"));
    }

    [Fact]
    public void Constructor_RejectsAmbiguousExtensions()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new DocumentExtractorRegistry([new FakeExtractor(".txt"), new FakeExtractor("txt")]));

        Assert.Contains("More than one", exception.Message);
    }

    [Fact]
    public void Resolve_SupportsNativeOpenXmlReaders()
    {
        DocumentExtractorRegistry registry = new(
            [new DocxDocumentExtractor(), new PptxDocumentExtractor()]);

        Assert.IsType<DocxDocumentExtractor>(registry.Resolve("policy.DOCX"));
        Assert.IsType<PptxDocumentExtractor>(registry.Resolve("overview.PPTX"));
    }

    private sealed class FakeExtractor(params string[] extensions) : IDocumentExtractor
    {
        public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(extensions);
        public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExtractedDocument(
                "fake",
                new IngestionDocument("fake"),
                []));
    }
}
