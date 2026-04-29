using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "image_content_type",
                table: "uploaded_document_draft",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "image_data",
                table: "uploaded_document_draft",
                type: "bytea",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "period_start",
                table: "tenant_subscription",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "period_end",
                table: "tenant_subscription",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAt",
                table: "tenant_membership",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeactivatedBy",
                table: "tenant_membership",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeactivatedReason",
                table: "tenant_membership",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "department_id",
                table: "tenant_membership",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "invitation",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevokedByMembershipId",
                table: "invitation",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenant_usage_snapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    ocr_pages_used = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    chatbot_messages_used = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    storage_used_bytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_usage_snapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_usage_snapshot_tenant_id_tenant",
                        column: x => x.id_tenant,
                        principalTable: "tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vendor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_code = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    verified_by_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_usage_snapshot_period",
                table: "tenant_usage_snapshot",
                columns: new[] { "id_tenant", "period_start", "period_end" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vendor_id_tenant_tax_code",
                table: "vendor",
                columns: new[] { "id_tenant", "tax_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vendor_is_verified",
                table: "vendor",
                column: "is_verified");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_usage_snapshot");

            migrationBuilder.DropTable(
                name: "vendor");

            migrationBuilder.DropColumn(
                name: "image_content_type",
                table: "uploaded_document_draft");

            migrationBuilder.DropColumn(
                name: "image_data",
                table: "uploaded_document_draft");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "tenant_membership");

            migrationBuilder.DropColumn(
                name: "DeactivatedBy",
                table: "tenant_membership");

            migrationBuilder.DropColumn(
                name: "DeactivatedReason",
                table: "tenant_membership");

            migrationBuilder.DropColumn(
                name: "department_id",
                table: "tenant_membership");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "invitation");

            migrationBuilder.DropColumn(
                name: "RevokedByMembershipId",
                table: "invitation");

            migrationBuilder.AlterColumn<DateTime>(
                name: "period_start",
                table: "tenant_subscription",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "date");

            migrationBuilder.AlterColumn<DateTime>(
                name: "period_end",
                table: "tenant_subscription",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "date");
        }
    }
}
