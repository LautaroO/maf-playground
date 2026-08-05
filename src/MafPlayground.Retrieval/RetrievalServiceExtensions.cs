using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Retrieval;

public static class RetrievalServiceExtensions
{
    public static IServiceCollection AddRetrievalCore(
        this IServiceCollection services,
        KnowledgeBaseCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        services.AddSingleton(catalog);
        services.AddSingleton<EmbeddingProviderRegistry>();
        services.AddSingleton<KnowledgeBaseRuntime>();
        services.AddSingleton<IDocumentExtractor, PdfDocumentExtractor>();
        services.AddSingleton<DocumentExtractorRegistry>();
        services.AddSingleton<DocumentChunker>();
        services.AddSingleton<IKnowledgeSearchFactory, KnowledgeSearchFactory>();
        services.AddSingleton<KnowledgeIngestionService>();
        return services;
    }
}
