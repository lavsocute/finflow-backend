using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RejectedByMembershipId",
                table: "payment",
                newName: "rejected_by_membership_id");

            migrationBuilder.RenameColumn(
                name: "RejectedAt",
                table: "payment",
                newName: "rejected_at");

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "payment",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                table: "payment",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by_membership_id",
                table: "payment",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "payment",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "payment_refund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    initiated_by_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_refund", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_refund_id_tenant",
                table: "payment_refund",
                column: "id_tenant");

            migrationBuilder.CreateIndex(
                name: "IX_payment_refund_payment_id",
                table: "payment_refund",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_refund_status",
                table: "payment_refund",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_refund");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "cancelled_by_membership_id",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "payment");

            migrationBuilder.RenameColumn(
                name: "rejected_by_membership_id",
                table: "payment",
                newName: "RejectedByMembershipId");

            migrationBuilder.RenameColumn(
                name: "rejected_at",
                table: "payment",
                newName: "RejectedAt");
        }
    }
}
