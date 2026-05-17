using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiCurrencyDocumentSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "amount_in_vnd",
                table: "payment",
                newName: "amount_in_base_currency");

            migrationBuilder.RenameColumn(
                name: "amount_in_vnd",
                table: "expense",
                newName: "amount_in_base_currency");

            migrationBuilder.AddColumn<string>(
                name: "base_currency_code",
                table: "uploaded_document_draft",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<string>(
                name: "currency_code",
                table: "uploaded_document_draft",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "uploaded_document_draft",
                type: "numeric(18,6)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "base_currency_code",
                table: "reviewed_document",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<string>(
                name: "currency_code",
                table: "reviewed_document",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "reviewed_document",
                type: "numeric(18,6)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "base_currency_code",
                table: "payment",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<string>(
                name: "base_currency_code",
                table: "expense",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "base_currency_code",
                table: "uploaded_document_draft");

            migrationBuilder.DropColumn(
                name: "currency_code",
                table: "uploaded_document_draft");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "uploaded_document_draft");

            migrationBuilder.DropColumn(
                name: "base_currency_code",
                table: "reviewed_document");

            migrationBuilder.DropColumn(
                name: "currency_code",
                table: "reviewed_document");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "reviewed_document");

            migrationBuilder.DropColumn(
                name: "base_currency_code",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "base_currency_code",
                table: "expense");

            migrationBuilder.RenameColumn(
                name: "amount_in_base_currency",
                table: "payment",
                newName: "amount_in_vnd");

            migrationBuilder.RenameColumn(
                name: "amount_in_base_currency",
                table: "expense",
                newName: "amount_in_vnd");
        }
    }
}
