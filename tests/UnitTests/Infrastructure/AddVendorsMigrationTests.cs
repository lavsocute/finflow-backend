using FinFlow.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace FinFlow.UnitTests.Infrastructure;

public sealed class AddVendorsMigrationTests
{
    [Fact]
    public void Up_GuardsDraftImageColumnsWhenSchemaIsPartiallyApplied()
    {
        var migration = new AddVendorsTestHarness();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        migration.ApplyUp(builder);

        var addColumnOperations = builder.Operations
            .OfType<AddColumnOperation>()
            .Select(operation => $"{operation.Table}.{operation.Name}")
            .ToArray();
        var createTableOperations = builder.Operations.OfType<CreateTableOperation>().Select(operation => operation.Name).ToArray();
        var createIndexOperations = builder.Operations.OfType<CreateIndexOperation>().Select(operation => operation.Name).ToArray();
        var sqlOperations = builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql).ToArray();

        Assert.DoesNotContain("uploaded_document_draft.image_content_type", addColumnOperations);
        Assert.DoesNotContain("uploaded_document_draft.image_data", addColumnOperations);
        Assert.DoesNotContain("tenant_membership.DeactivatedAt", addColumnOperations);
        Assert.DoesNotContain("tenant_membership.DeactivatedBy", addColumnOperations);
        Assert.DoesNotContain("tenant_membership.DeactivatedReason", addColumnOperations);
        Assert.DoesNotContain("tenant_membership.department_id", addColumnOperations);
        Assert.DoesNotContain("invitation.DepartmentId", addColumnOperations);
        Assert.DoesNotContain("invitation.RevokedByMembershipId", addColumnOperations);
        Assert.DoesNotContain("tenant_usage_snapshot", createTableOperations);
        Assert.DoesNotContain("vendor", createTableOperations);
        Assert.DoesNotContain("ux_tenant_usage_snapshot_period", createIndexOperations);
        Assert.DoesNotContain("IX_vendor_id_tenant_tax_code", createIndexOperations);
        Assert.DoesNotContain("IX_vendor_is_verified", createIndexOperations);
        Assert.Contains(sqlOperations, sql => sql.Contains("image_content_type", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("image_data", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("DeactivatedAt", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("DeactivatedBy", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("DeactivatedReason", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("department_id", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("DepartmentId", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("RevokedByMembershipId", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("CREATE TABLE IF NOT EXISTS tenant_usage_snapshot", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("CREATE TABLE IF NOT EXISTS vendor", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_usage_snapshot_period", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_vendor_id_tenant_tax_code\"", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, sql => sql.Contains("CREATE INDEX IF NOT EXISTS \"IX_vendor_is_verified\"", StringComparison.Ordinal));
        Assert.All(sqlOperations, sql =>
        {
            if (sql.Contains("uploaded_document_draft", StringComparison.Ordinal)
                || sql.Contains("tenant_membership", StringComparison.Ordinal)
                || sql.Contains("invitation", StringComparison.Ordinal))
            {
                Assert.Contains("IF NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    private sealed class AddVendorsTestHarness : AddVendors
    {
        public void ApplyUp(MigrationBuilder migrationBuilder) => Up(migrationBuilder);
    }
}
