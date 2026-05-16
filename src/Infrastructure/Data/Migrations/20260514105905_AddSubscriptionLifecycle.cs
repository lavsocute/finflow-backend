using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "billing_cycle_months",
                table: "tenant_subscription",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "canceled_at",
                table: "tenant_subscription",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "grace_period_days",
                table: "tenant_subscription",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTime>(
                name: "paused_at",
                table: "tenant_subscription",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "tenant_subscription",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "billing_cycle_months",
                table: "tenant_subscription");

            migrationBuilder.DropColumn(
                name: "canceled_at",
                table: "tenant_subscription");

            migrationBuilder.DropColumn(
                name: "grace_period_days",
                table: "tenant_subscription");

            migrationBuilder.DropColumn(
                name: "paused_at",
                table: "tenant_subscription");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "tenant_subscription");
        }
    }
}
