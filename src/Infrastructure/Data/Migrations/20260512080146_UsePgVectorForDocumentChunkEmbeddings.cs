using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UsePgVectorForDocumentChunkEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.Sql(@"CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.Sql(
                """
                ALTER TABLE document_chunks
                ALTER COLUMN "Embedding" TYPE vector(2048)
                USING (('[' || array_to_string("Embedding", ',') || ']')::vector(2048));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_IdTenant",
                table: "document_chunks",
                column: "IdTenant");

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_IdTenant_DepartmentId",
                table: "document_chunks",
                columns: new[] { "IdTenant", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_IdTenant_OwnerMembershipId",
                table: "document_chunks",
                columns: new[] { "IdTenant", "OwnerMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_IdTenant_Type",
                table: "document_chunks",
                columns: new[] { "IdTenant", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_document_chunks_IdTenant",
                table: "document_chunks");

            migrationBuilder.DropIndex(
                name: "IX_document_chunks_IdTenant_DepartmentId",
                table: "document_chunks");

            migrationBuilder.DropIndex(
                name: "IX_document_chunks_IdTenant_OwnerMembershipId",
                table: "document_chunks");

            migrationBuilder.DropIndex(
                name: "IX_document_chunks_IdTenant_Type",
                table: "document_chunks");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.Sql(
                """
                ALTER TABLE document_chunks
                ALTER COLUMN "Embedding" TYPE real[]
                USING (string_to_array(trim(both '[]' from "Embedding"::text), ',')::real[]);
                """);
        }
    }
}
