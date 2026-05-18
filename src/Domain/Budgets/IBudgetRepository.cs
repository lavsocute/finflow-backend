using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Budgets;

public record BudgetSummary(
    Guid Id,
    Guid IdTenant,
    Guid IdDepartment,
    string DepartmentName,
    int Month,
    int Year,
    decimal AllocatedAmount,
    decimal CommittedAmount,
    decimal SpentAmount,
    decimal? CarryOverFromPreviousMonth,
    string BaseCurrencyCode,
    BudgetEnforcementMode EnforcementMode,
    bool IsActive);

public interface IBudgetRepository
{
    Task<BudgetSummary?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<FinFlow.Domain.Entities.Budget?> GetEntityByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    Task<BudgetSummary?> GetByDepartmentAndPeriodAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default);
    Task<FinFlow.Domain.Entities.Budget?> GetEntityByDepartmentAndPeriodAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BudgetSummary>> GetByTenantIdAsync(Guid idTenant, int? month, int? year, Guid? departmentId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recompute the absolute spent total for a (department, month, year) tuple
    /// directly from confirmed expenses. Used by data-migration / admin tools
    /// only — NOT by the lifecycle pipeline (use Budget entity helpers there).
    /// </summary>
    Task<decimal> CalculateSpentAmountAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default);

    void Add(FinFlow.Domain.Entities.Budget budget);
    void Update(FinFlow.Domain.Entities.Budget budget);
}
