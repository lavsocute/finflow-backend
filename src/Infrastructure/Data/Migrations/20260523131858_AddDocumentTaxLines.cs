using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTaxLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reviewed_document_tax_line",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    reviewed_document_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviewed_document_tax_line", x => x.id);
                    table.ForeignKey(
                        name: "FK_reviewed_document_tax_line_reviewed_document_reviewed_docum~",
                        column: x => x.reviewed_document_id,
                        principalTable: "reviewed_document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "uploaded_document_draft_tax_line",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    uploaded_document_draft_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_uploaded_document_draft_tax_line", x => x.id);
                    table.ForeignKey(
                        name: "FK_uploaded_document_draft_tax_line_uploaded_document_draft_up~",
                        column: x => x.uploaded_document_draft_id,
                        principalTable: "uploaded_document_draft",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reviewed_document_tax_line_reviewed_document_id",
                table: "reviewed_document_tax_line",
                column: "reviewed_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_uploaded_document_draft_tax_line_uploaded_document_draft_id",
                table: "uploaded_document_draft_tax_line",
                column: "uploaded_document_draft_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reviewed_document_tax_line");

            migrationBuilder.DropTable(
                name: "uploaded_document_draft_tax_line");
        }
    }
}
