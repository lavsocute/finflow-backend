using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessageAnswerSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompressedSummary",
                table: "ChatSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "ChatSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "ChatSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAccessedAt",
                table: "ChatSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScopeBoundary",
                table: "ChatSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnswerSource",
                table: "ChatMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContextVersion",
                table: "ChatMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EffectiveDepartmentId",
                table: "ChatMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntentFamily",
                table: "ChatMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetrievedChunkIds",
                table: "ChatMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeContext",
                table: "ChatMessages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompressedSummary",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "LastAccessedAt",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "ScopeBoundary",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "AnswerSource",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ContextVersion",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "EffectiveDepartmentId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "IntentFamily",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "RetrievedChunkIds",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ScopeContext",
                table: "ChatMessages");
        }
    }
}
