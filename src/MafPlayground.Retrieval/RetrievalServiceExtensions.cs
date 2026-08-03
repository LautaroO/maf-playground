using MafPlayground.Retrieval.Documents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.Retrieval;

public static class RetrievalServiceExtensions
{
    public static IServiceCollection AddRetrievalCore(this IServiceCollection services, EmbeddingModelSelection selection)
    {
        services.AddSingleton(selection);
        services.AddOptions<RetrievalOptions>();
        services.AddSingleton<EmbeddingProviderRegistry>();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(provider => provider.GetRequiredService<EmbeddingProviderRegistry>().Create(selection));
        services.AddSingleton<IDocumentExtractor, PdfDocumentExtractor>();
        services.AddSingleton<DocumentExtractorRegistry>();
        services.AddSingleton<DocumentChunker>();
        services.AddSingleton<IKnowledgeSearch, KnowledgeSearchService>();
        services.AddSingleton<KnowledgeIngestionService>();
        return services;
    }
}
