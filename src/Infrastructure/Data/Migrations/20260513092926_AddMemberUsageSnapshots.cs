using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberUsageSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "member_usage_snapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    ocr_pages_used = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    chatbot_messages_used = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_usage_snapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_member_usage_snapshot_tenant_id_tenant",
                        column: x => x.id_tenant,
                        principalTable: "tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_usage_snapshot_tenant_membership_membership_id",
                        column: x => x.membership_id,
                        principalTable: "tenant_membership",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_member_usage_snapshot_membership_id",
                table: "member_usage_snapshot",
                column: "membership_id");

            migrationBuilder.CreateIndex(
                name: "ux_member_usage_snapshot_period",
                table: "member_usage_snapshot",
                columns: new[] { "id_tenant", "membership_id", "period_start", "period_end" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "member_usage_snapshot");
        }
    }
}
