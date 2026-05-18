using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorLinkToDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "id_vendor",
                table: "uploaded_document_draft",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "id_vendor",
                table: "reviewed_document",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_reviewed_document_id_tenant_id_vendor",
                table: "reviewed_document",
                columns: new[] { "id_tenant", "id_vendor" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reviewed_document_id_tenant_id_vendor",
                table: "reviewed_document");

            migrationBuilder.DropColumn(
                name: "id_vendor",
                table: "uploaded_document_draft");

            migrationBuilder.DropColumn(
                name: "id_vendor",
                table: "reviewed_document");
        }
    }
}
