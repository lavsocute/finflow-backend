using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatIntentExemplars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_intent_exemplars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExemplarText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    IntentMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IntentFamily = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IntentTask = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(2048)", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdTenant = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_intent_exemplars", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_intent_exemplars_IdTenant_IsActive",
                table: "chat_intent_exemplars",
                columns: new[] { "IdTenant", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_intent_exemplars_IsActive_EmbeddingModel",
                table: "chat_intent_exemplars",
                columns: new[] { "IsActive", "EmbeddingModel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_intent_exemplars");
        }
    }
}
