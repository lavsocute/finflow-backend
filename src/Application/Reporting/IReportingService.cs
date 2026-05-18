using FinFlow.Application.Reporting.DTOs;

namespace FinFlow.Application.Reporting;

/// <summary>
/// Aggregation queries for the reporting/analytics dashboard. All methods are
/// tenant-scoped via the active <c>ICurrentTenant</c>; callers must have
/// already validated the role gate (Manager+ for most, Accountant+ for
/// pending queue).
/// </summary>
public interface IReportingService
{
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
