namespace FinFlow.Domain.Budgets;

public record BudgetSummary(
    Guid Id,
    Guid IdTenant,
    Guid IdDepartment,
    string DepartmentName,
    int Month,
    int Year,
    decimal AllocatedAmount,
    decimal SpentAmount);

public interface IBudgetRepository
{
    Task<BudgetSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FinFlow.Domain.Entities.Budget?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BudgetSummary?> GetByDepartmentAndPeriodAsync(Guid departmentId, int month, int year, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BudgetSummary>> GetByTenantIdAsync(Guid idTenant, int? month, int? year, Guid? departmentId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid departmentId, int month, int year, CancellationToken cancellationToken = default);
    Task<decimal> CalculateSpentAmountAsync(Guid departmentId, int month, int year, CancellationToken cancellationToken = default);

    void Add(FinFlow.Domain.Entities.Budget budget);
    void Update(FinFlow.Domain.Entities.Budget budget);
}