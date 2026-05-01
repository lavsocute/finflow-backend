using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryPaymentExpense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "id_department",
                table: "reviewed_document",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "budgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    id_department = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    spent_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budgets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    category_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "expense",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    id_department = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_in_vnd = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    expense_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_by_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_tenant = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_department = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    amount_in_vnd = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    recorded_by_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    confirmed_by_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vendor_id_tenant_is_verified",
                table: "vendor",
                columns: new[] { "id_tenant", "is_verified" });

            migrationBuilder.CreateIndex(
                name: "IX_reviewed_document_id_department",
                table: "reviewed_document",
                column: "id_department");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_account_id_is_revoked",
                table: "refresh_token",
                columns: new[] { "account_id", "is_revoked" });

            migrationBuilder.CreateIndex(
                name: "IX_department_id_tenant_is_active",
                table: "department",
                columns: new[] { "id_tenant", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_budgets_dept_month_year",
                table: "budgets",
                columns: new[] { "id_department", "month", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_budgets_tenant_id",
                table: "budgets",
                column: "id_tenant");

            migrationBuilder.CreateIndex(
                name: "IX_category_id_tenant",
                table: "category",
                column: "id_tenant");

            migrationBuilder.CreateIndex(
                name: "IX_category_id_tenant_name",
                table: "category",
                columns: new[] { "id_tenant", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_category_is_active_display_order",
                table: "category",
                columns: new[] { "is_active", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_expense_category_id",
                table: "expense",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_document_id",
                table: "expense",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expense_id_department",
                table: "expense",
                column: "id_department");

            migrationBuilder.CreateIndex(
                name: "IX_expense_id_tenant",
                table: "expense",
                column: "id_tenant");

            migrationBuilder.CreateIndex(
                name: "IX_expense_payment_id",
                table: "expense",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expense_status",
                table: "expense",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_expense_year_month",
                table: "expense",
                columns: new[] { "year", "month" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_document_id",
                table: "payment",
                column: "document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_id_department",
                table: "payment",
                column: "id_department");

            migrationBuilder.CreateIndex(
                name: "IX_payment_id_tenant",
                table: "payment",
                column: "id_tenant");

            migrationBuilder.CreateIndex(
                name: "IX_payment_status",
                table: "payment",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budgets");

            migrationBuilder.DropTable(
                name: "category");

            migrationBuilder.DropTable(
                name: "expense");

            migrationBuilder.DropTable(
                name: "payment");

            migrationBuilder.DropIndex(
                name: "IX_vendor_id_tenant_is_verified",
                table: "vendor");

            migrationBuilder.DropIndex(
                name: "IX_reviewed_document_id_department",
                table: "reviewed_document");

            migrationBuilder.DropIndex(
                name: "IX_refresh_token_account_id_is_revoked",
                table: "refresh_token");

            migrationBuilder.DropIndex(
                name: "IX_department_id_tenant_is_active",
                table: "department");

            migrationBuilder.DropColumn(
                name: "id_department",
                table: "reviewed_document");
        }
    }
}
