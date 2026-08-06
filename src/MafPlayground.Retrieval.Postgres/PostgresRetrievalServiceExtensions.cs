using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MafPlayground.Retrieval.Postgres;

public static class PostgresRetrievalServiceExtensions
{
    public static IServiceCollection AddPostgresRetrieval(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PostgresRetrievalOptions>()
            .Bind(configuration.GetSection(PostgresRetrievalOptions.ConfigurationSectionName))
            .Validate(
                value => !string.IsNullOrWhiteSpace(value.ConnectionString),
                "A PostgreSQL connection string is required.")
            .ValidateOnStart();
        services.AddPooledDbContextFactory<KnowledgeDbContext>((provider, options) =>
        {
            string connectionString = provider.GetRequiredService<IOptions<PostgresRetrievalOptions>>().Value.ConnectionString;
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector());
        });
        services.AddSingleton<IKnowledgeStore, PostgresKnowledgeStore>();
        services.AddSingleton<IRetrievalDatabaseInitializer, PostgresRetrievalDatabaseInitializer>();
        return services;
    }
}
