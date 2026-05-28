using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLineItemVatFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tax_amount",
                table: "uploaded_document_draft_line_item",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_rate",
                table: "uploaded_document_draft_line_item",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "taxable_amount",
                table: "uploaded_document_draft_line_item",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_amount",
                table: "reviewed_document_line_item",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_rate",
                table: "reviewed_document_line_item",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "taxable_amount",
                table: "reviewed_document_line_item",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tax_amount",
                table: "uploaded_document_draft_line_item");

            migrationBuilder.DropColumn(
                name: "tax_rate",
                table: "uploaded_document_draft_line_item");

            migrationBuilder.DropColumn(
                name: "taxable_amount",
                table: "uploaded_document_draft_line_item");

            migrationBuilder.DropColumn(
                name: "tax_amount",
                table: "reviewed_document_line_item");

            migrationBuilder.DropColumn(
                name: "tax_rate",
                table: "reviewed_document_line_item");

            migrationBuilder.DropColumn(
                name: "taxable_amount",
                table: "reviewed_document_line_item");
        }
    }
}
