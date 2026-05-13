using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations;

[DbContext(typeof(global::FinFlow.Infrastructure.ApplicationDbContext))]
[Migration("20260503190500_AddAccountFullName")]
public partial class AddAccountFullName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE account
            ADD COLUMN IF NOT EXISTS full_name character varying(200);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE account
            DROP COLUMN IF EXISTS full_name;
            """);
    }
}
