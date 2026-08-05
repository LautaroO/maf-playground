using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MafPlayground.Retrieval.Postgres.Migrations;

[Migration("202608050001_AddDocumentMetadataIndex")]
[DbContext(typeof(KnowledgeDbContext))]
public sealed class AddDocumentMetadataIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "CREATE INDEX ix_knowledge_documents_metadata " +
            "ON knowledge_documents USING gin (\"MetadataJson\");");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP INDEX IF EXISTS ix_knowledge_documents_metadata;");
    }
}
