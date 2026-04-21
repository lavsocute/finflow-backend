using System.ComponentModel.DataAnnotations.Schema;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class TenantUsageSnapshot : Entity, IMultiTenant
{
    private TenantUsageSnapshot(
        Guid id,
        Guid idTenant,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        Id = id;
        IdTenant = idTenant;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        IsActive = true;
    }

    private TenantUsageSnapshot() { }

    public Guid IdTenant { get; private set; }

    [NotMapped]
    public Guid TenantId => IdTenant;

    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public int OcrPagesUsed { get; private set; }
    public int ChatbotMessagesUsed { get; private set; }
    public long StorageUsedBytes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Result<TenantUsageSnapshot> Create(Guid tenantId, DateOnly periodStart, DateOnly periodEnd)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantUsageSnapshot>(TenantUsageSnapshotErrors.TenantRequired);

        if (periodEnd < periodStart)
            return Result.Failure<TenantUsageSnapshot>(TenantUsageSnapshotErrors.InvalidPeriod);

        return Result.Success(new TenantUsageSnapshot(
            Guid.NewGuid(),
            tenantId,
            periodStart,
            periodEnd));
    }

    public Result RecordOcrUsage(int pageCount)
    {
        if (pageCount <= 0)
            return Result.Failure(TenantUsageSnapshotErrors.OcrUsageMustBePositive);

        OcrPagesUsed += pageCount;
        return Result.Success();
    }

    public Result RecordChatbotUsage(int messageCount)
    {
        if (messageCount <= 0)
            return Result.Failure(TenantUsageSnapshotErrors.ChatbotUsageMustBePositive);

        ChatbotMessagesUsed += messageCount;
        return Result.Success();
    }

    public Result SetStorageUsedBytes(long storageUsedBytes)
    {
        if (storageUsedBytes < 0)
            return Result.Failure(TenantUsageSnapshotErrors.StorageUsageCannotBeNegative);

        StorageUsedBytes = storageUsedBytes;
        return Result.Success();
    }
}
