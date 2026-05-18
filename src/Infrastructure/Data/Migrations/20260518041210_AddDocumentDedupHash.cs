using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentDedupHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dedup_hash",
                table: "reviewed_document",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reviewed_document_tenant_dedup_hash",
                table: "reviewed_document",
                columns: new[] { "id_tenant", "dedup_hash" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_id_tenant_created_at",
                table: "notification",
                columns: new[] { "id_tenant", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_recipient_unread_created",
                table: "notification",
                columns: new[] { "recipient_membership_id", "is_read", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification");

            migrationBuilder.DropIndex(
                name: "ix_reviewed_document_tenant_dedup_hash",
                table: "reviewed_document");

            migrationBuilder.DropColumn(
                name: "dedup_hash",
                table: "reviewed_document");
        }
    }
}
