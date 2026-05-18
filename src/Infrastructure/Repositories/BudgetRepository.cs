using FinFlow.Domain.Budgets;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Expenses;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class BudgetRepository : IBudgetRepository
{
    private readonly ApplicationDbContext _dbContext;

    public BudgetRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<BudgetSummary?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var budget = await _dbContext.Set<Budget>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.IdTenant == tenantId, cancellationToken);

        return budget is null ? null : await BuildSummaryAsync(budget, cancellationToken);
    }

    public Task<Budget?> GetEntityByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default) =>
        _dbContext.Set<Budget>()
            .FirstOrDefaultAsync(b => b.Id == id && b.IdTenant == tenantId, cancellationToken);

    public async Task<BudgetSummary?> GetByDepartmentAndPeriodAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default)
    {
        var budget = await _dbContext.Set<Budget>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b =>
                b.IdTenant == tenantId &&
                b.IdDepartment == departmentId &&
                b.Month == month &&
                b.Year == year,
                cancellationToken);

        return budget is null ? null : await BuildSummaryAsync(budget, cancellationToken);
    }

    public Task<Budget?> GetEntityByDepartmentAndPeriodAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) =>
        _dbContext.Set<Budget>()
            .FirstOrDefaultAsync(b =>
                b.IdTenant == tenantId &&
                b.IdDepartment == departmentId &&
                b.Month == month &&
                b.Year == year,
                cancellationToken);

    public async Task<IReadOnlyList<BudgetSummary>> GetByTenantIdAsync(Guid idTenant, int? month, int? year, Guid? departmentId, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Budget>()
            .AsNoTracking()
            .Where(b => b.IdTenant == idTenant);

        if (month.HasValue) query = query.Where(b => b.Month == month.Value);
        if (year.HasValue) query = query.Where(b => b.Year == year.Value);
        if (departmentId.HasValue) query = query.Where(b => b.IdDepartment == departmentId.Value);

        var budgets = await query.ToListAsync(cancellationToken);
        if (budgets.Count == 0) return [];

        var deptIds = budgets.Select(b => b.IdDepartment).Distinct().ToList();
        var deptNames = await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => deptIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name })
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        return budgets
            .Select(b => MapSummary(b, deptNames.GetValueOrDefault(b.IdDepartment, string.Empty)))
            .ToList();
    }

    public Task<bool> ExistsAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) =>
        _dbContext.Set<Budget>()
            .AnyAsync(b =>
                b.IdTenant == tenantId &&
                b.IdDepartment == departmentId &&
                b.Month == month &&
                b.Year == year,
                cancellationToken);

    public Task<decimal> CalculateSpentAmountAsync(Guid tenantId, Guid departmentId, int month, int year, CancellationToken cancellationToken = default) =>
        _dbContext.Set<Expense>()
            .AsNoTracking()
            .Where(e =>
                e.IdTenant == tenantId &&
                e.IdDepartment == departmentId &&
                e.Month == month &&
                e.Year == year &&
                e.Status == ExpenseStatus.Confirmed)
            .SumAsync(e => e.AmountInBaseCurrency, cancellationToken);

    public void Add(Budget budget) => _dbContext.Set<Budget>().Add(budget);
    public void Update(Budget budget) => _dbContext.Set<Budget>().Update(budget);

    private async Task<BudgetSummary> BuildSummaryAsync(Budget budget, CancellationToken cancellationToken)
    {
        var deptName = await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => d.Id == budget.IdDepartment)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(cancellationToken);
        return MapSummary(budget, deptName ?? string.Empty);
    }

    private static BudgetSummary MapSummary(Budget b, string departmentName) =>
        new(
            b.Id,
            b.IdTenant,
            b.IdDepartment,
            departmentName,
            b.Month,
            b.Year,
            b.AllocatedAmount,
            b.CommittedAmount,
            b.SpentAmount,
            b.CarryOverFromPreviousMonth,
            b.BaseCurrencyCode,
            b.EnforcementMode,
            b.IsActive);
}
