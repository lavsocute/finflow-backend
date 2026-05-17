using FinFlow.Domain.Expenses;

namespace FinFlow.Api.GraphQL.Expenses;

public sealed class ExpensePayload
{
    public Guid Id { get; set; }
    public string VendorName { get; set; } = null!;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public decimal AmountInBaseCurrency { get; set; }
    public string BaseCurrencyCode { get; set; } = null!;
    public int Month { get; set; }
    public int Year { get; set; }
    public DateTime ExpenseDate { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public static ExpensePayload FromSummary(ExpenseSummary summary) => new()
    {
        Id = summary.Id,
        VendorName = summary.VendorName,
        Amount = summary.Amount,
        CurrencyCode = summary.CurrencyCode,
        AmountInBaseCurrency = summary.AmountInBaseCurrency,
        BaseCurrencyCode = summary.BaseCurrencyCode,
        Month = summary.Month,
        Year = summary.Year,
        ExpenseDate = summary.ExpenseDate,
        Status = summary.Status.ToString(),
        CreatedAt = summary.CreatedAt
    };
}

public sealed class ExpenseSummaryPayload
{
    public decimal TotalAmountVnd { get; set; }
    public decimal BudgetAllocated { get; set; }
    public decimal BudgetSpent { get; set; }
    public decimal BudgetRemaining { get; set; }
    public bool IsOverBudget { get; set; }
    public bool IsNearLimit { get; set; }

    public static ExpenseSummaryPayload FromDto(Application.Expenses.DTOs.ExpenseSummaryDto dto) => new()
    {
        TotalAmountVnd = dto.TotalAmountVnd,
        BudgetAllocated = dto.BudgetAllocated,
        BudgetSpent = dto.BudgetSpent,
        BudgetRemaining = dto.BudgetRemaining,
        IsOverBudget = dto.IsOverBudget,
        IsNearLimit = dto.IsNearLimit
    };
}