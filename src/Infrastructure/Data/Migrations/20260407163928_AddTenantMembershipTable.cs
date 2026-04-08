using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantMembershipTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_membership",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_membership", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_membership_account_account_id",
                        column: x => x.account_id,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tenant_membership_tenant_id_tenant",
                        column: x => x.id_tenant,
                        principalTable: "tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_membership_account_id",
                table: "tenant_membership",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_membership_account_id_id_tenant",
                table: "tenant_membership",
                columns: new[] { "account_id", "id_tenant" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_membership_id_tenant",
                table: "tenant_membership",
                column: "id_tenant");

            migrationBuilder.Sql("""
                INSERT INTO tenant_membership ("Id", account_id, id_tenant, role, created_at, is_active)
                SELECT a."Id", a."Id", a.id_tenant, a.role, a.created_at, a.is_active
                FROM account AS a
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM tenant_membership AS tm
                    WHERE tm.account_id = a."Id"
                      AND tm.id_tenant = a.id_tenant
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_membership");
        }
    }
}
