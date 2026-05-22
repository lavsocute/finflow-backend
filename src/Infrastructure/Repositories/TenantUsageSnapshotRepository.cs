using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantUsageSnapshots;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class TenantUsageSnapshotRepository : ITenantUsageSnapshotRepository
{
    private readonly ApplicationDbContext _dbContext;

    public TenantUsageSnapshotRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<TenantUsageSnapshot?> GetByTenantAndPeriodAsync(
        Guid tenantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var trackedSnapshot = _dbContext.Set<TenantUsageSnapshot>().Local
            .FirstOrDefault(
                x => x.IdTenant == tenantId &&
                     x.PeriodStart == periodStart &&
                     x.PeriodEnd == periodEnd);

        if (trackedSnapshot is not null)
            return trackedSnapshot;

        return await _dbContext.Set<TenantUsageSnapshot>()
            .FirstOrDefaultAsync(
                x => x.IdTenant == tenantId &&
                     x.PeriodStart == periodStart &&
                     x.PeriodEnd == periodEnd,
                cancellationToken);
    }

    public async Task<TenantUsageSnapshot> GetOrCreateAsync(
        TenantUsageSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var trackedSnapshot = _dbContext.Set<TenantUsageSnapshot>().Local
            .FirstOrDefault(
                x => x.IdTenant == snapshot.IdTenant &&
                     x.PeriodStart == snapshot.PeriodStart &&
                     x.PeriodEnd == snapshot.PeriodEnd);

        if (trackedSnapshot is not null)
            return trackedSnapshot;

        if (!_dbContext.Database.IsRelational())
        {
            _dbContext.Set<TenantUsageSnapshot>().Add(snapshot);
            return snapshot;
        }

        var insertedRows = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO tenant_usage_snapshot
                ("Id", id_tenant, period_start, period_end, ocr_pages_used, chatbot_messages_used, storage_used_bytes, is_active)
            VALUES
                ({snapshot.Id}, {snapshot.IdTenant}, {snapshot.PeriodStart}, {snapshot.PeriodEnd}, {snapshot.OcrPagesUsed}, {snapshot.ChatbotMessagesUsed}, {snapshot.StorageUsedBytes}, {snapshot.IsActive})
            ON CONFLICT (id_tenant, period_start, period_end) DO NOTHING
            """,
            cancellationToken);

        if (insertedRows > 0)
            return await GetByTenantAndPeriodAsync(
                       snapshot.IdTenant,
                       snapshot.PeriodStart,
                       snapshot.PeriodEnd,
                       cancellationToken)
                   ?? throw new InvalidOperationException("Tenant usage snapshot was inserted but could not be reloaded.");

        return await GetByTenantAndPeriodAsync(
                   snapshot.IdTenant,
                   snapshot.PeriodStart,
                   snapshot.PeriodEnd,
                   cancellationToken)
               ?? throw new InvalidOperationException("Tenant usage snapshot insert conflicted but no existing snapshot could be loaded.");
    }

    public void Add(TenantUsageSnapshot snapshot) => _dbContext.Set<TenantUsageSnapshot>().Add(snapshot);

    public void Update(TenantUsageSnapshot snapshot) => _dbContext.Set<TenantUsageSnapshot>().Update(snapshot);
}
