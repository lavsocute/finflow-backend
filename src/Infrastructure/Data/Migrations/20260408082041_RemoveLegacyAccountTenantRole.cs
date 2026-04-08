using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyAccountTenantRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_account_tenant_id_tenant",
                table: "account");

            migrationBuilder.DropIndex(
                name: "IX_account_id_tenant",
                table: "account");

            migrationBuilder.DropColumn(
                name: "id_tenant",
                table: "account");

            migrationBuilder.DropColumn(
                name: "role",
                table: "account");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "id_tenant",
                table: "account",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "account",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_account_id_tenant",
                table: "account",
                column: "id_tenant");

            migrationBuilder.AddForeignKey(
                name: "FK_account_tenant_id_tenant",
                table: "account",
                column: "id_tenant",
                principalTable: "tenant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
