using Microsoft.EntityFrameworkCore;

namespace MafPlayground.Retrieval.Postgres;

public sealed class PostgresRetrievalDatabaseInitializer(IDbContextFactory<KnowledgeDbContext> contextFactory) : IRetrievalDatabaseInitializer
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using KnowledgeDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
