using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TENANT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NAME = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    TENANT_CODE = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TENANCY_MODEL = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CONNECTION_STRING = table.Column<string>(type: "text", nullable: true),
                    CURRENCY = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "VND"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TENANT", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DEPARTMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ID_TENANT = table.Column<Guid>(type: "uuid", nullable: false),
                    PARENT_ID = table.Column<Guid>(type: "uuid", nullable: true),
                    NAME = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DEPARTMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DEPARTMENT_DEPARTMENT_PARENT_ID",
                        column: x => x.PARENT_ID,
                        principalTable: "DEPARTMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DEPARTMENT_TENANT_ID_TENANT",
                        column: x => x.ID_TENANT,
                        principalTable: "TENANT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ACCOUNT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EMAIL = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PASSWORD_HASH = table.Column<string>(type: "text", nullable: false),
                    ROLE = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ID_TENANT = table.Column<Guid>(type: "uuid", nullable: false),
                    ID_DEPARTMENT = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    LastModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ACCOUNT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ACCOUNT_DEPARTMENT_ID_DEPARTMENT",
                        column: x => x.ID_DEPARTMENT,
                        principalTable: "DEPARTMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ACCOUNT_TENANT_ID_TENANT",
                        column: x => x.ID_TENANT,
                        principalTable: "TENANT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ACCOUNT_EMAIL",
                table: "ACCOUNT",
                column: "EMAIL",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ACCOUNT_ID_DEPARTMENT",
                table: "ACCOUNT",
                column: "ID_DEPARTMENT");

            migrationBuilder.CreateIndex(
                name: "IX_ACCOUNT_ID_TENANT",
                table: "ACCOUNT",
                column: "ID_TENANT");

            migrationBuilder.CreateIndex(
                name: "IX_DEPARTMENT_ID_TENANT",
                table: "DEPARTMENT",
                column: "ID_TENANT");

            migrationBuilder.CreateIndex(
                name: "IX_DEPARTMENT_PARENT_ID",
                table: "DEPARTMENT",
                column: "PARENT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_TENANT_TENANT_CODE",
                table: "TENANT",
                column: "TENANT_CODE",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ACCOUNT");

            migrationBuilder.DropTable(
                name: "DEPARTMENT");

            migrationBuilder.DropTable(
                name: "TENANT");
        }
    }
}
