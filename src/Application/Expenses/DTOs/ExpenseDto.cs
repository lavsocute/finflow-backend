namespace FinFlow.Application.Expenses.DTOs;

public sealed record ExpenseDto(
    Guid Id,
    Guid IdTenant,
    Guid IdDepartment,
    string DepartmentName,
    Guid DocumentId,
    Guid PaymentId,
    Guid IdCategory,
    string CategoryName,
    string VendorName,
    decimal Amount,
    string CurrencyCode,
    decimal AmountInVnd,
    int Month,
    int Year,
    DateTime ExpenseDate,
    string Status,
    DateTime CreatedAt);

public sealed record ExpenseSummaryDto(
    decimal TotalAmountVnd,
    decimal ByCategory,
    decimal BudgetAllocated,
    decimal BudgetSpent,
    decimal BudgetRemaining,
    bool IsOverBudget,
    bool IsNearLimit);