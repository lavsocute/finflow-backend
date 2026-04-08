using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_by_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invitation_tenant_id_tenant",
                        column: x => x.id_tenant,
                        principalTable: "tenant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invitation_tenant_membership_invited_by_membership_id",
                        column: x => x.invited_by_membership_id,
                        principalTable: "tenant_membership",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invitation_id_tenant",
                table: "invitation",
                column: "id_tenant");

            migrationBuilder.CreateIndex(
                name: "IX_invitation_id_tenant_email",
                table: "invitation",
                columns: new[] { "id_tenant", "email" });

            migrationBuilder.CreateIndex(
                name: "IX_invitation_invited_by_membership_id",
                table: "invitation",
                column: "invited_by_membership_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitation_token_hash",
                table: "invitation",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invitation");
        }
    }
}
