using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkRefreshTokenToMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "membership_id",
                table: "refresh_token",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE refresh_token AS rt
                SET membership_id = tm."Id"
                FROM account AS a
                INNER JOIN tenant_membership AS tm
                    ON tm.account_id = a."Id"
                   AND tm.id_tenant = a.id_tenant
                WHERE rt.account_id = a."Id";
                """);

            migrationBuilder.Sql("""
                DELETE FROM refresh_token
                WHERE membership_id IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "membership_id",
                table: "refresh_token",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_membership_id",
                table: "refresh_token",
                column: "membership_id");

            migrationBuilder.AddForeignKey(
                name: "FK_refresh_token_tenant_membership_membership_id",
                table: "refresh_token",
                column: "membership_id",
                principalTable: "tenant_membership",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_refresh_token_tenant_membership_membership_id",
                table: "refresh_token");

            migrationBuilder.DropIndex(
                name: "IX_refresh_token_membership_id",
                table: "refresh_token");

            migrationBuilder.DropColumn(
                name: "membership_id",
                table: "refresh_token");
        }
    }
}
