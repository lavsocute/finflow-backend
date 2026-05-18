using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceBudgetForEnterpriseControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "base_currency_code",
                table: "budgets",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<decimal>(
                name: "carry_over_from_prev",
                table: "budgets",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "committed_amount",
                table: "budgets",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "enforcement_mode",
                table: "budgets",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "SoftBlock");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "budgets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_budgets_tenant_period_active",
                table: "budgets",
                columns: new[] { "id_tenant", "year", "month", "is_active" });

            migrationBuilder.AddForeignKey(
                name: "FK_budgets_department_id_department",
                table: "budgets",
                column: "id_department",
                principalTable: "department",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_budgets_tenant_id_tenant",
                table: "budgets",
                column: "id_tenant",
                principalTable: "tenant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Backfill base_currency_code from the owning tenant for any
            // existing rows where the tenant currency != "VND". The column
            // default ("VND") covers the common case; this UPDATE handles
            // multi-currency tenants that may have shipped with Task 5.
            migrationBuilder.Sql(@"
                UPDATE budgets b
                SET base_currency_code = t.currency
                FROM tenant t
                WHERE b.id_tenant = t.""Id""
                  AND t.currency IS NOT NULL
                  AND t.currency <> b.base_currency_code;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_budgets_department_id_department",
                table: "budgets");

            migrationBuilder.DropForeignKey(
                name: "FK_budgets_tenant_id_tenant",
                table: "budgets");

            migrationBuilder.DropIndex(
                name: "ix_budgets_tenant_period_active",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "base_currency_code",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "carry_over_from_prev",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "committed_amount",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "enforcement_mode",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "budgets");
        }
    }
}
