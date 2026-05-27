using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260524093000_NormalizeUnsupportedGuestRoles")]
public partial class NormalizeUnsupportedGuestRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE tenant_membership
            SET role = 'Staff'
            WHERE role IN ('Guest', 'GUEST', 'guest');
            """);

        migrationBuilder.Sql("""
            UPDATE invitation
            SET role = 'Staff'
            WHERE role IN ('Guest', 'GUEST', 'guest');
            """);

        migrationBuilder.Sql("""
            UPDATE tenant_settings
            SET escalation_approver_role = 'TenantAdmin'
            WHERE escalation_approver_role IN ('Guest', 'GUEST', 'guest');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
