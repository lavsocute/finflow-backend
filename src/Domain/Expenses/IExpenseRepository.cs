namespace FinFlow.Domain.Expenses;

public record ExpenseSummary(
    Guid Id,
    Guid IdTenant,
    Guid IdDepartment,
    Guid DocumentId,
    Guid PaymentId,
    Guid IdCategory,
    string VendorName,
    decimal Amount,
    CurrencyCode CurrencyCode,
    decimal AmountInVnd,
    int Month,
    int Year,
    DateTime ExpenseDate,
    ExpenseStatus Status,
    Guid CreatedByMembershipId,
    DateTime CreatedAt);

public interface IExpenseRepository
{
    Task<ExpenseSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ExpenseSummary?> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExpenseSummary>> GetByDepartmentAndPeriodAsync(Guid departmentId, int month, int year, ExpenseStatus? status = null, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalSpentByDepartmentAndPeriodAsync(Guid departmentId, int month, int year, ExpenseStatus status, CancellationToken cancellationToken = default);
    Task<Expense?> GetEntityByIdAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(Expense expense);
    void Update(Expense expense);
}