using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "password_reset_challenge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    otp_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reason_revoked = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    otp_attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_otp_attempts = table.Column<int>(type: "integer", nullable: false),
                    cooldown_seconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_challenge", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_challenge_account_id_created_at",
                table: "password_reset_challenge",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_challenge_token_hash",
                table: "password_reset_challenge",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_reset_challenge");
        }
    }
}
