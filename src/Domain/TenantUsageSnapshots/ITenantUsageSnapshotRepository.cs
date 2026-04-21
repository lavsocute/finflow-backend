using FinFlow.Domain.Entities;

namespace FinFlow.Domain.TenantUsageSnapshots;

public interface ITenantUsageSnapshotRepository
{
    Task<TenantUsageSnapshot?> GetByTenantAndPeriodAsync(
        Guid tenantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    void Add(TenantUsageSnapshot snapshot);
    void Update(TenantUsageSnapshot snapshot);
}
