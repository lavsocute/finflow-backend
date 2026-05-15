using FinFlow.Domain.Entities;

namespace FinFlow.Application.Common.Abstractions;

public interface IMemberUsageService
{
    /// <summary>
    /// Gets the current member usage snapshot for the supplied tenant-owned membership and period.
    /// Creates a new snapshot when none exists after validating that the membership exists, is active,
    /// and belongs to the tenant identified by <paramref name="tenantId" />.
    /// </summary>
    Task<MemberUsageSnapshot> GetCurrentUsageAsync(
        Guid tenantId,
        Guid membershipId,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records OCR usage against the validated tenant-owned membership snapshot for the given period.
    /// Creates the period snapshot when missing after membership ownership validation succeeds.
    /// </summary>
    Task RecordOcrUsageAsync(
        Guid tenantId,
        Guid membershipId,
        int pageCount,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records chatbot usage against the validated tenant-owned membership snapshot for the given period.
    /// Creates the period snapshot when missing after membership ownership validation succeeds.
    /// </summary>
    Task RecordChatbotUsageAsync(
        Guid tenantId,
        Guid membershipId,
        int messageCount,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken = default);
}
