using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations;

public partial class AddTenantUsageSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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

        migrationBuilder.CreateIndex(
            name: "ux_tenant_usage_snapshot_period",
            table: "tenant_usage_snapshot",
            columns: new[] { "id_tenant", "period_start", "period_end" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "tenant_usage_snapshot");
    }
}
