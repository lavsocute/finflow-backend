using System.ComponentModel.DataAnnotations.Schema;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class MemberUsageSnapshot : Entity, IMultiTenant, ISoftDeletable
{
    private MemberUsageSnapshot(
        Guid id,
        Guid idTenant,
        Guid membershipId,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        Id = id;
        IdTenant = idTenant;
        MembershipId = membershipId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        IsActive = true;
    }

    private MemberUsageSnapshot() { }

    public Guid IdTenant { get; private set; }

    [NotMapped]
    public Guid TenantId => IdTenant;

    public Guid MembershipId { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public int OcrPagesUsed { get; private set; }
    public int ChatbotMessagesUsed { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Concurrency token mapped to PostgreSQL xmin system column.
    /// </summary>
    public uint Version { get; private set; }

    public static Result<MemberUsageSnapshot> Create(
        Guid tenantId,
        Guid membershipId,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<MemberUsageSnapshot>(MemberUsageSnapshotErrors.TenantRequired);

        if (membershipId == Guid.Empty)
            return Result.Failure<MemberUsageSnapshot>(MemberUsageSnapshotErrors.MembershipRequired);

        if (periodEnd < periodStart)
            return Result.Failure<MemberUsageSnapshot>(MemberUsageSnapshotErrors.InvalidPeriod);

        return Result.Success(new MemberUsageSnapshot(
            Guid.NewGuid(),
            tenantId,
            membershipId,
            periodStart,
            periodEnd));
    }

    public Result RecordOcrUsage(int pageCount)
    {
        if (pageCount <= 0)
            return Result.Failure(MemberUsageSnapshotErrors.OcrUsageMustBePositive);

        OcrPagesUsed += pageCount;
        return Result.Success();
    }

    public Result RecordChatbotUsage(int messageCount)
    {
        if (messageCount <= 0)
            return Result.Failure(MemberUsageSnapshotErrors.ChatbotUsageMustBePositive);

        ChatbotMessagesUsed += messageCount;
        return Result.Success();
    }
}
