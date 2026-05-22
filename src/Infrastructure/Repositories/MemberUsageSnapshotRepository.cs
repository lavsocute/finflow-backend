using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantUsageSnapshots;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class MemberUsageSnapshotRepository : IMemberUsageSnapshotRepository
{
    private readonly ApplicationDbContext _dbContext;

    public MemberUsageSnapshotRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<MemberUsageSnapshot?> GetByMembershipAndPeriodAsync(
        Guid tenantId,
        Guid membershipId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var trackedSnapshot = _dbContext.Set<MemberUsageSnapshot>().Local
            .FirstOrDefault(
                x => x.IdTenant == tenantId &&
                     x.MembershipId == membershipId &&
                     x.PeriodStart == periodStart &&
                     x.PeriodEnd == periodEnd);

        if (trackedSnapshot is not null)
            return trackedSnapshot;

        return await _dbContext.Set<MemberUsageSnapshot>()
            .FirstOrDefaultAsync(
                x => x.IdTenant == tenantId &&
                     x.MembershipId == membershipId &&
                     x.PeriodStart == periodStart &&
                     x.PeriodEnd == periodEnd,
                cancellationToken);
    }

    public async Task<MemberUsageSnapshot> GetOrCreateAsync(
        MemberUsageSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var trackedSnapshot = _dbContext.Set<MemberUsageSnapshot>().Local
            .FirstOrDefault(
                x => x.IdTenant == snapshot.IdTenant &&
                     x.MembershipId == snapshot.MembershipId &&
                     x.PeriodStart == snapshot.PeriodStart &&
                     x.PeriodEnd == snapshot.PeriodEnd);

        if (trackedSnapshot is not null)
            return trackedSnapshot;

        if (!_dbContext.Database.IsRelational())
        {
            _dbContext.Set<MemberUsageSnapshot>().Add(snapshot);
            return snapshot;
        }

        var insertedRows = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO member_usage_snapshot
                ("Id", id_tenant, membership_id, period_start, period_end, ocr_pages_used, chatbot_messages_used, is_active)
            VALUES
                ({snapshot.Id}, {snapshot.IdTenant}, {snapshot.MembershipId}, {snapshot.PeriodStart}, {snapshot.PeriodEnd}, {snapshot.OcrPagesUsed}, {snapshot.ChatbotMessagesUsed}, {snapshot.IsActive})
            ON CONFLICT (id_tenant, membership_id, period_start, period_end) DO NOTHING
            """,
            cancellationToken);

        if (insertedRows > 0)
            return await GetByMembershipAndPeriodAsync(
                       snapshot.IdTenant,
                       snapshot.MembershipId,
                       snapshot.PeriodStart,
                       snapshot.PeriodEnd,
                       cancellationToken)
                   ?? throw new InvalidOperationException("Member usage snapshot was inserted but could not be reloaded.");

        return await GetByMembershipAndPeriodAsync(
                   snapshot.IdTenant,
                   snapshot.MembershipId,
                   snapshot.PeriodStart,
                   snapshot.PeriodEnd,
                   cancellationToken)
               ?? throw new InvalidOperationException("Member usage snapshot insert conflicted but no existing snapshot could be loaded.");
    }

    public void Add(MemberUsageSnapshot snapshot) => _dbContext.Set<MemberUsageSnapshot>().Add(snapshot);

    public void Update(MemberUsageSnapshot snapshot) => _dbContext.Set<MemberUsageSnapshot>().Update(snapshot);
}
