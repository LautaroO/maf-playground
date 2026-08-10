using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.CLI.Extensions;

public static class RetrievalCompositionExtensions
{
    public static IServiceCollection AddConfiguredRetrieval(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Dictionary<string, KnowledgeBaseOptions> definitions = configuration
            .GetSection(KnowledgeBaseCatalogOptions.ConfigurationSectionName)
            .Get<Dictionary<string, KnowledgeBaseOptions>>() ?? [];
        KnowledgeBaseCatalog catalog = new(new KnowledgeBaseCatalogOptions
        {
            KnowledgeBases = definitions,
        });
        catalog.ValidateEmbeddingDimensions(
            KnowledgeDbContext.EmbeddingDimensions,
            "the PostgreSQL retrieval store");

        services.AddRetrievalCore(catalog);
        services.AddPostgresRetrieval(configuration);
        return services;
    }
}
