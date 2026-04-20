using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Data.Migrations;

[DbContext(typeof(global::FinFlow.Infrastructure.ApplicationDbContext))]
[Migration("20260418154000_AddReviewedDocumentRejectionReason")]
public partial class AddReviewedDocumentRejectionReason : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE reviewed_document
            ADD COLUMN IF NOT EXISTS rejection_reason character varying(500);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE reviewed_document
            DROP COLUMN IF EXISTS rejection_reason;
            """);
    }
}
