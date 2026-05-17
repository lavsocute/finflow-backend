using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds a GIN index on tsvector(simple, Content) for full-text keyword search.
    /// Used by the hybrid retrieval pipeline (vector + keyword fusion) in ChatService.
    /// </summary>
    public partial class AddDocumentChunkFtsIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_document_chunks_content_fts " +
                "ON document_chunks USING gin(to_tsvector('simple', \"Content\"));");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_document_chunks_content_fts;");
        }
    }
}
