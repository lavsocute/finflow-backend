using FinFlow.Domain.Entities;

namespace FinFlow.Domain.TenantUsageSnapshots;

public interface ITenantUsageSnapshotRepository
{
    Task<TenantUsageSnapshot?> GetByTenantAndPeriodAsync(
        Guid tenantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    Task<TenantUsageSnapshot> GetOrCreateAsync(
        TenantUsageSnapshot snapshot,
        CancellationToken cancellationToken = default);

    void Add(TenantUsageSnapshot snapshot);
    void Update(TenantUsageSnapshot snapshot);
}
