using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MafPlayground.Retrieval.Postgres;

public sealed class PostgresKnowledgeStore(IDbContextFactory<KnowledgeDbContext> contextFactory) : IKnowledgeStore
{
    public async Task<StoredDocumentState?> GetDocumentStateAsync(string collection, string sourceId, CancellationToken cancellationToken)
    {
        await using KnowledgeDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents.AsNoTracking()
            .Where(value => value.Collection.Name == collection && value.SourceId == sourceId)
            .Select(value => new StoredDocumentState(value.ContentHash, value.EmbeddingIdentity, value.ChunkingIdentity))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task ReplaceDocumentAsync(KnowledgeDocument document, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken)
    {
        await using KnowledgeDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        KnowledgeCollectionEntity? collection = await db.Collections.SingleOrDefaultAsync(value => value.Name == document.Collection, cancellationToken);
        if (collection is null)
        {
            collection = new() { Id = Guid.NewGuid(), Name = document.Collection, EmbeddingIdentity = document.EmbeddingIdentity };
            db.Collections.Add(collection);
        }
        else if (!string.Equals(collection.EmbeddingIdentity, document.EmbeddingIdentity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Collection '{document.Collection}' uses '{collection.EmbeddingIdentity}', not '{document.EmbeddingIdentity}'. Use another collection or re-index it.");
        }

        KnowledgeDocumentEntity? entity = await db.Documents.Include(value => value.Chunks)
            .SingleOrDefaultAsync(value => value.CollectionId == collection.Id && value.SourceId == document.SourceId, cancellationToken);
        if (entity is null)
        {
            entity = new() { Id = Guid.NewGuid(), Collection = collection, CollectionId = collection.Id, SourceId = document.SourceId, Title = document.Title, Path = document.Path, ContentHash = document.ContentHash, EmbeddingIdentity = document.EmbeddingIdentity, ChunkingIdentity = document.ChunkingIdentity };
            db.Documents.Add(entity);
        }
        else
        {
            db.Chunks.RemoveRange(entity.Chunks);
            entity.Title = document.Title;
            entity.Path = document.Path;
            entity.ContentHash = document.ContentHash;
            entity.EmbeddingIdentity = document.EmbeddingIdentity;
            entity.ChunkingIdentity = document.ChunkingIdentity;
        }

        entity.Chunks = chunks.Select(chunk => new KnowledgeChunkEntity
        {
            Id = Guid.NewGuid(),
            Document = entity,
            DocumentId = entity.Id,
            ChunkIndex = chunk.Index,
            Text = chunk.Text,
            PageNumber = chunk.PageNumber,
            SectionName = chunk.SectionName,
            Embedding = new Vector(chunk.Embedding),
        }).ToList();

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(KnowledgeSearchRequest request, CancellationToken cancellationToken)
    {
        await using KnowledgeDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        Vector query = new(request.Embedding);
        double maximumDistance = 1 - request.MinimumSimilarity;
        return await db.Chunks.AsNoTracking()
            .Where(value => value.Document.Collection.Name == request.Collection)
            .Select(value => new
            {
                value.Document.SourceId,
                value.Document.Title,
                value.Text,
                value.PageNumber,
                value.SectionName,
                Distance = value.Embedding.CosineDistance(query),
            })
            .Where(value => value.Distance <= maximumDistance)
            .OrderBy(value => value.Distance)
            .Take(request.TopK)
            .Select(value => new KnowledgeSearchResult(value.SourceId, value.Title, value.Text, value.PageNumber, value.SectionName, 1 - value.Distance))
            .ToListAsync(cancellationToken);
    }
}
