using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Repositories;

namespace FinFlow.UnitTests.Infrastructure;

public sealed class UsageSnapshotRepositorySqlTests
{
    [Fact]
    public void TenantUsageSnapshot_InsertSql_QuotesPascalCaseIdColumn()
    {
        var snapshot = TenantUsageSnapshot.Create(
            Guid.NewGuid(),
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 6, 1)).Value;

        var command = TenantUsageSnapshotRepository.CreateInsertCommand(snapshot);

        Assert.Contains("(\"Id\", id_tenant, period_start, period_end", command.Format);
        Assert.DoesNotContain("(id, id_tenant", command.Format);
    }

    [Fact]
    public void MemberUsageSnapshot_InsertSql_QuotesPascalCaseIdColumn()
    {
        var snapshot = MemberUsageSnapshot.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 6, 1)).Value;

        var command = MemberUsageSnapshotRepository.CreateInsertCommand(snapshot);

        Assert.Contains("(\"Id\", id_tenant, membership_id, period_start, period_end", command.Format);
        Assert.DoesNotContain("(id, id_tenant", command.Format);
    }
}
