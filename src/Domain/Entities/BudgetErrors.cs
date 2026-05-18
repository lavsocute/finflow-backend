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
    public static readonly Error CurrencyRequired = new("Budget.CurrencyRequired", "Base currency code is required (3-letter ISO 4217).");
    public static readonly Error CurrencyImmutable = new("Budget.CurrencyImmutable", "Base currency cannot be changed after creation.");
    public static readonly Error InsufficientCommitment = new("Budget.InsufficientCommitment", "Cannot release more committed amount than is currently committed.");
    public static readonly Error InsufficientSpent = new("Budget.InsufficientSpent", "Cannot reverse more spent amount than is currently spent.");
    public static readonly Error InvalidEnforcementMode = new("Budget.InvalidEnforcementMode", "Enforcement mode is not recognized.");
    public static readonly Error AlreadyArchived = new("Budget.AlreadyArchived", "Budget is already archived.");
    public static readonly Error CannotArchiveActive = new("Budget.CannotArchiveActive", "Budget cannot be archived while it has movements in the current period.");
    public static readonly Error HardBlocked = new("Budget.HardBlocked", "This action would exceed the department's hard-blocked budget. Adjust the amount or change the budget enforcement mode.");
    public static readonly Error OverrideRequired = new("Budget.OverrideRequired", "This action exceeds the budget. Provide an override justification to proceed.");
    public static readonly Error OverrideJustificationRequired = new("Budget.OverrideJustificationRequired", "Override justification cannot be empty.");
}