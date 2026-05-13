using FinFlow.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace FinFlow.UnitTests.Infrastructure;

public sealed class AddAccountFullNameMigrationTests
{
    [Fact]
    public void Up_AddsFullNameColumnThroughGuardedSql()
    {
        var migration = new AddAccountFullNameTestHarness();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        migration.ApplyUp(builder);

        Assert.Empty(builder.Operations.OfType<AddColumnOperation>());
        var sql = Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("ALTER TABLE account", sql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS full_name", sql, StringComparison.Ordinal);
    }

    private sealed class AddAccountFullNameTestHarness : AddAccountFullName
    {
        public void ApplyUp(MigrationBuilder migrationBuilder) => Up(migrationBuilder);
    }
}
