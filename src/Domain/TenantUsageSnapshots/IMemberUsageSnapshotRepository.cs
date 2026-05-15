using FinFlow.Domain.Entities;

namespace FinFlow.Domain.TenantUsageSnapshots;

public interface IMemberUsageSnapshotRepository
{
    Task<MemberUsageSnapshot?> GetByMembershipAndPeriodAsync(
        Guid tenantId,
        Guid membershipId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    Task<MemberUsageSnapshot> GetOrCreateAsync(
        MemberUsageSnapshot snapshot,
        CancellationToken cancellationToken = default);

    void Add(MemberUsageSnapshot snapshot);
    void Update(MemberUsageSnapshot snapshot);
}
