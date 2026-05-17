using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Expenses;

public static class ExpenseErrors
{
    public static readonly Error NotFound = new("Expense.NotFound", "Expense not found");
    public static readonly Error AlreadyProcessed = new("Expense.AlreadyProcessed", "Expense has already been processed");
    public static readonly Error RejectionReasonRequired = new("Expense.RejectionReasonRequired", "Rejection reason is required");
    public static readonly Error TenantRequired = new("Expense.TenantRequired", "Tenant ID is required");
    public static readonly Error DepartmentRequired = new("Expense.DepartmentRequired", "Department ID is required");
    public static readonly Error CategoryRequired = new("Expense.CategoryRequired", "Category ID is required");
    public static readonly Error InvalidAmount = new("Expense.InvalidAmount", "Amount must be greater than zero");
    public static readonly Error InvalidMonth = new("Expense.InvalidMonth", "Month must be between 1 and 12");
    public static readonly Error InvalidYear = new("Expense.InvalidYear", "Year must be between 2000 and 2100");
    public static readonly Error NotRejected = new("Expense.NotRejected", "Only rejected expenses can be reopened");
    public static readonly Error ReopenWindowExpired = new("Expense.ReopenWindowExpired", "Reopen window has expired");
    public static readonly Error ReopenReasonRequired = new("Expense.ReopenReasonRequired", "Reopen reason is required");
    public static readonly Error ReopenReasonTooLong = new("Expense.ReopenReasonTooLong", "Reopen reason must be 500 characters or less");
    public static readonly Error ReopenedByRequired = new("Expense.ReopenedByRequired", "Reopened-by membership ID is required");
}