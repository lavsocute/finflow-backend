using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantApprovalRequestFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_owner",
                table: "tenant_membership",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "company_name",
                table: "tenant",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                table: "tenant",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenant_approval_request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    company_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    business_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    employee_count = table.Column<int>(type: "integer", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "VND"),
                    tenancy_model = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_approval_request", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_approval_request_account_requested_by_id",
                        column: x => x.requested_by_id,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_approval_request_requested_by_id_status",
                table: "tenant_approval_request",
                columns: new[] { "requested_by_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_approval_request_status",
                table: "tenant_approval_request",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_approval_request_tenant_code",
                table: "tenant_approval_request",
                column: "tenant_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_approval_request");

            migrationBuilder.DropColumn(
                name: "is_owner",
                table: "tenant_membership");

            migrationBuilder.DropColumn(
                name: "company_name",
                table: "tenant");

            migrationBuilder.DropColumn(
                name: "tax_code",
                table: "tenant");
        }
    }
}
