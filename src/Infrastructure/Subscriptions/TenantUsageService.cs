using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantUsageSnapshots;

namespace FinFlow.Infrastructure.Subscriptions;

public sealed class TenantUsageService : ITenantUsageService
{
    private readonly ITenantUsageSnapshotRepository _tenantUsageSnapshotRepository;

    public TenantUsageService(ITenantUsageSnapshotRepository tenantUsageSnapshotRepository)
    {
        _tenantUsageSnapshotRepository = tenantUsageSnapshotRepository;
    }

    public async Task<TenantUsageSnapshot> GetCurrentUsageAsync(
        Guid tenantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var (snapshot, _) = await GetOrCreateSnapshotAsync(tenantId, periodStart, periodEnd, cancellationToken);
        return snapshot;
    }

    public async Task RecordOcrUsageAsync(
        Guid tenantId,
        int pageCount,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var (snapshot, isNew) = await GetOrCreateSnapshotAsync(tenantId, periodStart, periodEnd, cancellationToken);
        var result = snapshot.RecordOcrUsage(pageCount);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Description);

        if (!isNew)
            _tenantUsageSnapshotRepository.Update(snapshot);
    }

    public async Task RecordChatbotUsageAsync(
        Guid tenantId,
        int messageCount,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var (snapshot, isNew) = await GetOrCreateSnapshotAsync(tenantId, periodStart, periodEnd, cancellationToken);
        var result = snapshot.RecordChatbotUsage(messageCount);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Description);

        if (!isNew)
            _tenantUsageSnapshotRepository.Update(snapshot);
    }

    public async Task SetStorageUsedBytesAsync(
        Guid tenantId,
        long storageUsedBytes,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var (snapshot, isNew) = await GetOrCreateSnapshotAsync(tenantId, periodStart, periodEnd, cancellationToken);
        var result = snapshot.SetStorageUsedBytes(storageUsedBytes);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Description);

        if (!isNew)
            _tenantUsageSnapshotRepository.Update(snapshot);
    }

    private async Task<(TenantUsageSnapshot Snapshot, bool IsNew)> GetOrCreateSnapshotAsync(
        Guid tenantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken)
    {
        var existingSnapshot = await _tenantUsageSnapshotRepository.GetByTenantAndPeriodAsync(
            tenantId,
            periodStart,
            periodEnd,
            cancellationToken);

        if (existingSnapshot is not null)
            return (existingSnapshot, false);

        var createdSnapshotResult = TenantUsageSnapshot.Create(tenantId, periodStart, periodEnd);
        if (createdSnapshotResult.IsFailure)
            throw new InvalidOperationException(createdSnapshotResult.Error.Description);

        var createdSnapshot = createdSnapshotResult.Value;
        _tenantUsageSnapshotRepository.Add(createdSnapshot);
        return (createdSnapshot, true);
    }
}
