using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentDiscountAndLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "discount_amount",
                table: "uploaded_document_draft_line_item",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_percent",
                table: "uploaded_document_draft_line_item",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "uploaded_document_draft",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "document_discount_amount",
                table: "uploaded_document_draft",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "document_discount_percent",
                table: "uploaded_document_draft",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "uploaded_document_draft",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_amount",
                table: "reviewed_document_line_item",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_percent",
                table: "reviewed_document_line_item",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "document_discount_amount",
                table: "reviewed_document",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "document_discount_percent",
                table: "reviewed_document",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "reviewed_document",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "discount_amount",
                table: "uploaded_document_draft_line_item");

            migrationBuilder.DropColumn(
                name: "discount_percent",
                table: "uploaded_document_draft_line_item");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "uploaded_document_draft");

            migrationBuilder.DropColumn(
                name: "document_discount_amount",
                table: "uploaded_document_draft");

            migrationBuilder.DropColumn(
                name: "document_discount_percent",
                table: "uploaded_document_draft");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "uploaded_document_draft");

            migrationBuilder.DropColumn(
                name: "discount_amount",
                table: "reviewed_document_line_item");

            migrationBuilder.DropColumn(
                name: "discount_percent",
                table: "reviewed_document_line_item");

            migrationBuilder.DropColumn(
                name: "document_discount_amount",
                table: "reviewed_document");

            migrationBuilder.DropColumn(
                name: "document_discount_percent",
                table: "reviewed_document");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "reviewed_document");
        }
    }
}
