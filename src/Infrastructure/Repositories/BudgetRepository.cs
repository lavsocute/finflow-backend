using FinFlow.Domain.Entities;
using FinFlow.Domain.Budgets;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class BudgetRepository : IBudgetRepository
{
    private readonly ApplicationDbContext _dbContext;

    public BudgetRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<BudgetSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var budget = await _dbContext.Set<Budget>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (budget == null)
            return null;

        var department = await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => d.Id == budget.IdDepartment)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var spentAmount = await CalculateSpentAmountAsync(budget.IdDepartment, budget.Month, budget.Year, cancellationToken);

        return new BudgetSummary(
            budget.Id,
            budget.IdTenant,
            budget.IdDepartment,
            department ?? string.Empty,
            budget.Month,
            budget.Year,
            budget.AllocatedAmount,
            spentAmount);
    }

    public async Task<Budget?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Budget>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<BudgetSummary?> GetByDepartmentAndPeriodAsync(Guid departmentId, int month, int year, CancellationToken cancellationToken = default)
    {
        var budget = await _dbContext.Set<Budget>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(b =>
                b.IdDepartment == departmentId &&
                b.Month == month &&
                b.Year == year,
                cancellationToken);

        if (budget == null)
            return null;

        var department = await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => d.Id == budget.IdDepartment)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var spentAmount = await CalculateSpentAmountAsync(budget.IdDepartment, budget.Month, budget.Year, cancellationToken);

        return new BudgetSummary(
            budget.Id,
            budget.IdTenant,
            budget.IdDepartment,
            department ?? string.Empty,
            budget.Month,
            budget.Year,
            budget.AllocatedAmount,
            spentAmount);
    }

    public async Task<IReadOnlyList<BudgetSummary>> GetByTenantIdAsync(Guid idTenant, int? month, int? year, Guid? departmentId, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Budget>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => b.IdTenant == idTenant);

        if (month.HasValue)
            query = query.Where(b => b.Month == month.Value);
        if (year.HasValue)
            query = query.Where(b => b.Year == year.Value);
        if (departmentId.HasValue)
            query = query.Where(b => b.IdDepartment == departmentId.Value);

        var budgets = await query
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        if (budgets.Count == 0)
            return Array.Empty<BudgetSummary>();

        var departmentIds = budgets.Select(b => b.IdDepartment).Distinct().ToList();
        var departments = await _dbContext.Set<Department>()
            .AsNoTracking()
            .Where(d => departmentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var budgetKeys = budgets.Select(b => (b.IdDepartment, b.Month, b.Year)).Distinct().ToList();
        var spentByKey = new Dictionary<(Guid IdDepartment, int Month, int Year), decimal>();

        foreach (var key in budgetKeys)
        {
            var spent = await _dbContext.Set<FinFlow.Domain.Expenses.Expense>()
                .AsNoTracking()
                .Where(e =>
                    e.IdDepartment == key.IdDepartment &&
                    e.Month == key.Month &&
                    e.Year == key.Year &&
                    e.Status == FinFlow.Domain.Expenses.ExpenseStatus.Confirmed)
                .SumAsync(e => e.AmountInVnd, cancellationToken);
            spentByKey[key] = spent;
        }

        var results = new List<BudgetSummary>(budgets.Count);
        foreach (var budget in budgets)
        {
            departments.TryGetValue(budget.IdDepartment, out var deptName);
            spentByKey.TryGetValue((budget.IdDepartment, budget.Month, budget.Year), out var spentAmount);

            results.Add(new BudgetSummary(
                budget.Id,
                budget.IdTenant,
                budget.IdDepartment,
                deptName ?? string.Empty,
                budget.Month,
                budget.Year,
                budget.AllocatedAmount,
                spentAmount));
        }

        return results;
    }

    public async Task<bool> ExistsAsync(Guid departmentId, int month, int year, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Budget>()
            .IgnoreQueryFilters()
            .AnyAsync(b =>
                b.IdDepartment == departmentId &&
                b.Month == month &&
                b.Year == year,
                cancellationToken);

    public async Task<decimal> CalculateSpentAmountAsync(Guid departmentId, int month, int year, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<FinFlow.Domain.Expenses.Expense>()
            .AsNoTracking()
            .Where(e =>
                e.IdDepartment == departmentId &&
                e.Month == month &&
                e.Year == year &&
                e.Status == FinFlow.Domain.Expenses.ExpenseStatus.Confirmed)
            .SumAsync(e => e.AmountInVnd, cancellationToken);
    }

    public void Add(Budget budget) => _dbContext.Set<Budget>().Add(budget);
    public void Update(Budget budget) => _dbContext.Set<Budget>().Update(budget);
}