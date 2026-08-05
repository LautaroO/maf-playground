using MafPlayground.Retrieval;
using MafPlayground.Retrieval.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MafPlayground.IntegrationTests;

public sealed class PostgresKnowledgeStoreTests
{
    [PostgresFact]
    public async Task MigrateReplaceAndSearch_RoundTripsVectorEvidence()
    {
        string connectionString = Environment.GetEnvironmentVariable("RAG_TEST_CONNECTION_STRING")!;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{PostgresRetrievalOptions.ConfigurationSectionName}:ConnectionString"] = connectionString,
            })
            .Build();
        ServiceProvider services = new ServiceCollection()
            .AddPostgresRetrieval(configuration)
            .BuildServiceProvider();
        await using (services)
        {
            await services.GetRequiredService<IRetrievalDatabaseInitializer>().MigrateAsync();
            IKnowledgeStore store = services.GetRequiredService<IKnowledgeStore>();
            string collection = $"integration-{Guid.NewGuid():N}";
            float[] vector = new float[768];
            vector[0] = 1;
            KnowledgeMetadata metadata = KnowledgeMetadata.Create(
                new Dictionary<string, string>
                {
                    ["audience"] = "customer",
                    ["product"] = "support",
                });
            KnowledgeDocument document = new(collection, "manual/help.pdf", "Help", "/manual/help.pdf", "hash", "test:model/768", "test-chunks", metadata);
            await store.ReplaceDocumentAsync(document, [new(0, "Reset the account from Settings.", 3, "Page 3", vector)], CancellationToken.None);

            StoredDocumentState? state = await store.GetDocumentStateAsync(
                collection,
                "manual/help.pdf",
                CancellationToken.None);
            IReadOnlyList<KnowledgeSearchResult> results = await store.SearchAsync(
                new(
                    collection,
                    vector,
                    3,
                    0.9,
                    KnowledgeMetadata.Create(new Dictionary<string, string>
                    {
                        ["audience"] = "customer",
                    })),
                CancellationToken.None);
            IReadOnlyList<KnowledgeSearchResult> excluded = await store.SearchAsync(
                new(
                    collection,
                    vector,
                    3,
                    0.9,
                    KnowledgeMetadata.Create(new Dictionary<string, string>
                    {
                        ["audience"] = "internal",
                    })),
                CancellationToken.None);

            Assert.NotNull(state);
            Assert.Equal(metadata, state.Metadata);
            KnowledgeSearchResult result = Assert.Single(results);
            Assert.Equal("manual/help.pdf", result.SourceId);
            Assert.Equal(3, result.PageNumber);
            Assert.Empty(excluded);
        }
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RAG_TEST_CONNECTION_STRING")))
        {
            Skip = "Set RAG_TEST_CONNECTION_STRING to run the PostgreSQL/pgvector integration test.";
        }
    }
}
