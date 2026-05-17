using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseReopenLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RejectionReason",
                table: "expense",
                newName: "rejection_reason");

            migrationBuilder.AddColumn<DateTime>(
                name: "rejected_at",
                table: "expense",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rejected_by_membership_id",
                table: "expense",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reopened_at",
                table: "expense",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reopened_by_membership_id",
                table: "expense",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reopened_reason",
                table: "expense",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "expense",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rejected_at",
                table: "expense");

            migrationBuilder.DropColumn(
                name: "rejected_by_membership_id",
                table: "expense");

            migrationBuilder.DropColumn(
                name: "reopened_at",
                table: "expense");

            migrationBuilder.DropColumn(
                name: "reopened_by_membership_id",
                table: "expense");

            migrationBuilder.DropColumn(
                name: "reopened_reason",
                table: "expense");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "expense");

            migrationBuilder.RenameColumn(
                name: "rejection_reason",
                table: "expense",
                newName: "RejectionReason");
        }
    }
}
