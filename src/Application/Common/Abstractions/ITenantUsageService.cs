using FinFlow.Domain.Entities;

namespace FinFlow.Application.Common.Abstractions;

public interface ITenantUsageService
{
    Task<TenantUsageSnapshot> GetCurrentUsageAsync(
        Guid tenantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    Task RecordOcrUsageAsync(
        Guid tenantId,
        int pageCount,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    Task RecordChatbotUsageAsync(
        Guid tenantId,
        int messageCount,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    Task SetStorageUsedBytesAsync(
        Guid tenantId,
        long storageUsedBytes,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);
}
