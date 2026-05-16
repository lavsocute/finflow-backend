using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "tenant_usage_snapshot",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "refresh_token",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "payment",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByMembershipId",
                table: "payment",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "member_usage_snapshot",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "expense",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "budgets",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "tenant_usage_snapshot");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "refresh_token");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "RejectedByMembershipId",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "member_usage_snapshot");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "expense");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "budgets");
        }
    }
}
