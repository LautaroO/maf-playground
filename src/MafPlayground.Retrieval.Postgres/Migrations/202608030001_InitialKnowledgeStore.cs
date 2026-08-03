using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace MafPlayground.Retrieval.Postgres.Migrations;

[Migration("202608030001_InitialKnowledgeStore")]
[DbContext(typeof(KnowledgeDbContext))]
public sealed class InitialKnowledgeStore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");
        migrationBuilder.Sql("""
            CREATE TABLE knowledge_collections (
                "Id" uuid PRIMARY KEY,
                "Name" varchar(200) NOT NULL UNIQUE,
                "EmbeddingIdentity" varchar(500) NOT NULL
            );
            CREATE TABLE knowledge_documents (
                "Id" uuid PRIMARY KEY,
                "CollectionId" uuid NOT NULL REFERENCES knowledge_collections("Id") ON DELETE CASCADE,
                "SourceId" varchar(1000) NOT NULL,
                "Title" varchar(500) NOT NULL,
                "Path" text NOT NULL,
                "ContentHash" varchar(128) NOT NULL,
                "EmbeddingIdentity" varchar(500) NOT NULL,
                "ChunkingIdentity" varchar(500) NOT NULL,
                "MetadataJson" jsonb NOT NULL DEFAULT '{}',
                UNIQUE ("CollectionId", "SourceId")
            );
            CREATE TABLE knowledge_chunks (
                "Id" uuid PRIMARY KEY,
                "DocumentId" uuid NOT NULL REFERENCES knowledge_documents("Id") ON DELETE CASCADE,
                "ChunkIndex" integer NOT NULL,
                "Text" text NOT NULL,
                "PageNumber" integer NULL,
                "SectionName" text NULL,
                "Embedding" vector(768) NOT NULL,
                "MetadataJson" jsonb NOT NULL DEFAULT '{}',
                UNIQUE ("DocumentId", "ChunkIndex")
            );
            CREATE INDEX ix_knowledge_chunks_embedding ON knowledge_chunks USING hnsw ("Embedding" vector_cosine_ops);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS knowledge_chunks; DROP TABLE IF EXISTS knowledge_documents; DROP TABLE IF EXISTS knowledge_collections;");
    }
}
