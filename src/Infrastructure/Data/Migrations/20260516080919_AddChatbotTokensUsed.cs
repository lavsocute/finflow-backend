using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatbotTokensUsed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "chatbot_tokens_used",
                table: "tenant_usage_snapshot",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "chatbot_tokens_used",
                table: "member_usage_snapshot",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chatbot_tokens_used",
                table: "tenant_usage_snapshot");

            migrationBuilder.DropColumn(
                name: "chatbot_tokens_used",
                table: "member_usage_snapshot");
        }
    }
}
