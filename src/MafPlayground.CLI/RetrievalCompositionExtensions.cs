using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MafPlayground.CLI;

public static class RetrievalCompositionExtensions
{
    public static IServiceCollection AddConfiguredRetrieval(
        this IServiceCollection services,
        IConfiguration configuration,
        EmbeddingModelSelection selection)
    {
        services.Configure<RetrievalOptions>(configuration.GetSection(RetrievalOptions.ConfigurationSectionName));
        services.AddRetrievalCore(selection);
        services.AddPostgresRetrieval(configuration);
        return services;
    }
}
