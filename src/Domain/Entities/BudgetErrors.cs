using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class BudgetErrors
{
    public static readonly Error NotFound = new("Budget.NotFound", "The budget with the specified ID was not found");
    public static readonly Error DepartmentRequired = new("Budget.DepartmentRequired", "Department ID is required");
    public static readonly Error TenantRequired = new("Budget.TenantRequired", "Tenant ID is required");
    public static readonly Error InvalidMonth = new("Budget.InvalidMonth", "Month must be between 1 and 12");
    public static readonly Error InvalidYear = new("Budget.InvalidYear", "Year must be between 2000 and 2100");
    public static readonly Error InvalidAmount = new("Budget.InvalidAmount", "Amount must be greater than or equal to 0");
    public static readonly Error DuplicateBudget = new("Budget.Duplicate", "A budget for this department and month/year already exists");
}