using FinFlow.Domain.Expenses;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class ExpenseRepository : IExpenseRepository
{
    private readonly ApplicationDbContext _dbContext;

    public ExpenseRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<ExpenseSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Expense>()
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new ExpenseSummary(
                e.Id,
                e.IdTenant,
                e.IdDepartment,
                e.DocumentId,
                e.PaymentId,
                e.IdCategory,
                e.VendorName,
                e.Amount,
                e.CurrencyCode,
                e.AmountInBaseCurrency,
                e.BaseCurrencyCode,
                e.Month,
                e.Year,
                e.ExpenseDate,
                e.Status,
                e.CreatedByMembershipId,
                e.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<ExpenseSummary?> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Expense>()
            .AsNoTracking()
            .Where(e => e.PaymentId == paymentId)
            .Select(e => new ExpenseSummary(
                e.Id,
                e.IdTenant,
                e.IdDepartment,
                e.DocumentId,
                e.PaymentId,
                e.IdCategory,
                e.VendorName,
                e.Amount,
                e.CurrencyCode,
                e.AmountInBaseCurrency,
                e.BaseCurrencyCode,
                e.Month,
                e.Year,
                e.ExpenseDate,
                e.Status,
                e.CreatedByMembershipId,
                e.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ExpenseSummary>> GetByDepartmentAndPeriodAsync(Guid departmentId, int month, int year, ExpenseStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Expense>()
            .AsNoTracking()
            .Where(e => e.IdDepartment == departmentId && e.Month == month && e.Year == year);

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);

        return await query
            .OrderByDescending(e => e.ExpenseDate)
            .Select(e => new ExpenseSummary(
                e.Id,
                e.IdTenant,
                e.IdDepartment,
                e.DocumentId,
                e.PaymentId,
                e.IdCategory,
                e.VendorName,
                e.Amount,
                e.CurrencyCode,
                e.AmountInBaseCurrency,
                e.BaseCurrencyCode,
                e.Month,
                e.Year,
                e.ExpenseDate,
                e.Status,
                e.CreatedByMembershipId,
                e.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<decimal> GetTotalSpentByDepartmentAndPeriodAsync(Guid departmentId, int month, int year, ExpenseStatus status, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Expense>()
            .AsNoTracking()
            .Where(e => e.IdDepartment == departmentId && e.Month == month && e.Year == year && e.Status == status)
            .SumAsync(e => e.AmountInBaseCurrency, cancellationToken);

    public async Task<Expense?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Expense>()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public void Add(Expense expense) => _dbContext.Set<Expense>().Add(expense);
    public void Update(Expense expense) => _dbContext.Set<Expense>().Update(expense);
}