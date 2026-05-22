using FinFlow.Application.Reporting.DTOs;

namespace FinFlow.Application.Reporting;

/// <summary>
/// Tenant-scoped reporting queries used by dashboards and chat-safe aggregate
/// answers. Callers are responsible for enforcing capability/role gates before
/// invoking broader aggregate methods.
/// </summary>
public interface IReportingService
{
    /// <summary>
    /// Returns a date-precise confirmed-expense summary for a single membership.
    /// This method is safe for own-scope chat/reporting flows when the caller
    /// has already authorized access to the target membership.
    /// </summary>
    Task<OwnExpenseSummaryDto> GetOwnExpenseSummaryAsync(
        Guid tenantId,
        Guid membershipId,
        ReportingPeriod period,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a tenant-wide or department-scoped aggregate summary. This is a
    /// broader reporting surface and requires higher-privilege authorization.
    /// </summary>
    Task<ExpenseSummaryDto> GetExpenseSummaryAsync(
        Guid tenantId,
        ReportingPeriod period,
        Guid? departmentScope,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BudgetUtilizationDto>> GetBudgetUtilizationAsync(
        Guid tenantId,
        int month,
        int year,
        Guid? departmentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TopVendorDto>> GetTopVendorsAsync(
        Guid tenantId,
        ReportingPeriod period,
        Guid? departmentScope,
        Guid? ownerMembershipScope,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TopEmployeeDto>> GetTopEmployeesAsync(
        Guid tenantId,
        ReportingPeriod period,
        Guid? departmentScope,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingPaymentItemDto>> GetPendingPaymentQueueAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonthlyTrendPointDto>> GetMonthlyTrendAsync(
        Guid tenantId,
        int monthCount,
        Guid? departmentScope,
        CancellationToken cancellationToken = default);
}
