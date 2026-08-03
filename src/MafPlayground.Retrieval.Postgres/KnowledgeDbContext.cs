using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace MafPlayground.Retrieval.Postgres;

public sealed class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options) : DbContext(options)
{
    public DbSet<KnowledgeCollectionEntity> Collections => Set<KnowledgeCollectionEntity>();
    public DbSet<KnowledgeDocumentEntity> Documents => Set<KnowledgeDocumentEntity>();
    public DbSet<KnowledgeChunkEntity> Chunks => Set<KnowledgeChunkEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<KnowledgeCollectionEntity>(entity =>
        {
            entity.ToTable("knowledge_collections");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.Name).IsUnique();
            entity.Property(value => value.Name).HasMaxLength(200);
            entity.Property(value => value.EmbeddingIdentity).HasMaxLength(500);
        });
        modelBuilder.Entity<KnowledgeDocumentEntity>(entity =>
        {
            entity.ToTable("knowledge_documents");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.CollectionId, value.SourceId }).IsUnique();
            entity.Property(value => value.SourceId).HasMaxLength(1000);
            entity.Property(value => value.Title).HasMaxLength(500);
            entity.Property(value => value.ContentHash).HasMaxLength(128);
            entity.Property(value => value.EmbeddingIdentity).HasMaxLength(500);
            entity.Property(value => value.ChunkingIdentity).HasMaxLength(500);
            entity.Property(value => value.MetadataJson).HasColumnType("jsonb");
            entity.HasOne(value => value.Collection).WithMany(value => value.Documents).HasForeignKey(value => value.CollectionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<KnowledgeChunkEntity>(entity =>
        {
            entity.ToTable("knowledge_chunks");
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => new { value.DocumentId, value.ChunkIndex }).IsUnique();
            entity.Property(value => value.Embedding).HasColumnType("vector(768)");
            entity.Property(value => value.MetadataJson).HasColumnType("jsonb");
            entity.HasIndex(value => value.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
            entity.HasOne(value => value.Document).WithMany(value => value.Chunks).HasForeignKey(value => value.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class KnowledgeCollectionEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string EmbeddingIdentity { get; set; }
    public List<KnowledgeDocumentEntity> Documents { get; set; } = [];
}

public sealed class KnowledgeDocumentEntity
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public required string SourceId { get; set; }
    public required string Title { get; set; }
    public required string Path { get; set; }
    public required string ContentHash { get; set; }
    public required string EmbeddingIdentity { get; set; }
    public required string ChunkingIdentity { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public KnowledgeCollectionEntity Collection { get; set; } = null!;
    public List<KnowledgeChunkEntity> Chunks { get; set; } = [];
}

public sealed class KnowledgeChunkEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public required string Text { get; set; }
    public int? PageNumber { get; set; }
    public string? SectionName { get; set; }
    public Vector Embedding { get; set; } = null!;
    public string MetadataJson { get; set; } = "{}";
    public KnowledgeDocumentEntity Document { get; set; } = null!;
}
