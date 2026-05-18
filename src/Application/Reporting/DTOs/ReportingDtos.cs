namespace FinFlow.Application.Reporting.DTOs;

/// <summary>
/// Top-level summary for a tenant + period. Always reported in tenant base
/// currency. <see cref="ByCurrency"/> is populated only when the period
/// contains expenses in more than one currency.
/// </summary>
public sealed record ExpenseSummaryDto(
    int ExpenseCount,
    decimal TotalInBaseCurrency,
    string BaseCurrencyCode,
    IReadOnlyList<ExpenseSummaryGroupDto> ByCategory,
    IReadOnlyList<ExpenseSummaryGroupDto> ByDepartment,
    IReadOnlyList<ExpenseSummaryByCurrencyDto> ByCurrency);

public sealed record ExpenseSummaryGroupDto(
    Guid? KeyId,
    string KeyName,
    decimal AmountInBaseCurrency,
    int ExpenseCount);

public sealed record ExpenseSummaryByCurrencyDto(
    string CurrencyCode,
    decimal NativeAmount,
    decimal AmountInBaseCurrency,
    int ExpenseCount);

/// <summary>
/// Per-department budget vs spent for a single (month, year). All amounts in
/// tenant base currency.
/// </summary>
public sealed record BudgetUtilizationDto(
    Guid DepartmentId,
    string DepartmentName,
    int Month,
    int Year,
    decimal Allocated,
    decimal Spent,
    decimal Remaining,
    decimal UtilizationPercent,    // 0..n; > 100 means over-budget
    bool IsApproachingLimit,       // >= 90%
    bool IsOverBudget,
    string BaseCurrencyCode);

/// <summary>
/// Top vendor by reimbursement total. Source: ReviewedDocument joined to
/// Vendor (where IdVendor not null). Documents without a linked vendor are
/// excluded from this view — they show under "Untracked vendors" in the UI.
/// </summary>
public sealed record TopVendorDto(
    Guid VendorId,
    string VendorName,
    string TaxCode,
    bool IsVerified,
    int DocumentCount,
    decimal TotalAmountInBaseCurrency,
    string BaseCurrencyCode);

/// <summary>
/// Top reimbursed employee. Aggregates confirmed expenses per membership.
/// </summary>
public sealed record TopEmployeeDto(
    Guid MembershipId,
    Guid AccountId,
    string EmployeeName,
    string DepartmentName,
    int ExpenseCount,
    decimal TotalAmountInBaseCurrency,
    string BaseCurrencyCode);

/// <summary>
/// Single row in the accountant's "things waiting on me" view. Sorted oldest
/// first, with explicit age in days so frontend can render alert badges.
/// </summary>
public sealed record PendingPaymentItemDto(
    Guid PaymentId,
    Guid DocumentId,
    string Reference,
    string EmployeeName,
    string DepartmentName,
    decimal Amount,
    string CurrencyCode,
    decimal AmountInBaseCurrency,
    string BaseCurrencyCode,
    string PaymentMethod,
    DateTime RecordedAt,
    int AgeDays);

/// <summary>
/// One bucket on a monthly trend chart. <see cref="ExpenseTotal"/> is
/// confirmed expenses in base currency. <see cref="DocumentCount"/> counts
/// documents (not line items).
/// </summary>
public sealed record MonthlyTrendPointDto(
    int Year,
    int Month,
    decimal ExpenseTotal,
    int DocumentCount,
    string BaseCurrencyCode);
