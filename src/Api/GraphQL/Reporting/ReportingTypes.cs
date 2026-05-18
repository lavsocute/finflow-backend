using FinFlow.Application.Reporting.DTOs;

namespace FinFlow.Api.GraphQL.Reporting;

// One-to-one wrappers around DTOs so we don't leak Application types into the
// GraphQL schema and so we control field naming/nullability if needed later.

public sealed class ExpenseSummaryPayload
{
    public int ExpenseCount { get; init; }
    public decimal TotalInBaseCurrency { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;
    public IReadOnlyList<ExpenseSummaryGroupPayload> ByCategory { get; init; } = [];
    public IReadOnlyList<ExpenseSummaryGroupPayload> ByDepartment { get; init; } = [];
    public IReadOnlyList<ExpenseSummaryByCurrencyPayload> ByCurrency { get; init; } = [];

    public static ExpenseSummaryPayload From(ExpenseSummaryDto dto) => new()
    {
        ExpenseCount = dto.ExpenseCount,
        TotalInBaseCurrency = dto.TotalInBaseCurrency,
        BaseCurrencyCode = dto.BaseCurrencyCode,
        ByCategory = dto.ByCategory.Select(ExpenseSummaryGroupPayload.From).ToList(),
        ByDepartment = dto.ByDepartment.Select(ExpenseSummaryGroupPayload.From).ToList(),
        ByCurrency = dto.ByCurrency.Select(ExpenseSummaryByCurrencyPayload.From).ToList(),
    };
}

public sealed class ExpenseSummaryGroupPayload
{
    public Guid? KeyId { get; init; }
    public string KeyName { get; init; } = string.Empty;
    public decimal AmountInBaseCurrency { get; init; }
    public int ExpenseCount { get; init; }

    public static ExpenseSummaryGroupPayload From(ExpenseSummaryGroupDto dto) => new()
    {
        KeyId = dto.KeyId,
        KeyName = dto.KeyName,
        AmountInBaseCurrency = dto.AmountInBaseCurrency,
        ExpenseCount = dto.ExpenseCount,
    };
}

public sealed class ExpenseSummaryByCurrencyPayload
{
    public string CurrencyCode { get; init; } = string.Empty;
    public decimal NativeAmount { get; init; }
    public decimal AmountInBaseCurrency { get; init; }
    public int ExpenseCount { get; init; }

    public static ExpenseSummaryByCurrencyPayload From(ExpenseSummaryByCurrencyDto dto) => new()
    {
        CurrencyCode = dto.CurrencyCode,
        NativeAmount = dto.NativeAmount,
        AmountInBaseCurrency = dto.AmountInBaseCurrency,
        ExpenseCount = dto.ExpenseCount,
    };
}

public sealed class BudgetUtilizationPayload
{
    public Guid DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public int Month { get; init; }
    public int Year { get; init; }
    public decimal Allocated { get; init; }
    public decimal Spent { get; init; }
    public decimal Remaining { get; init; }
    public decimal UtilizationPercent { get; init; }
    public bool IsApproachingLimit { get; init; }
    public bool IsOverBudget { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;

    public static BudgetUtilizationPayload From(BudgetUtilizationDto dto) => new()
    {
        DepartmentId = dto.DepartmentId,
        DepartmentName = dto.DepartmentName,
        Month = dto.Month,
        Year = dto.Year,
        Allocated = dto.Allocated,
        Spent = dto.Spent,
        Remaining = dto.Remaining,
        UtilizationPercent = dto.UtilizationPercent,
        IsApproachingLimit = dto.IsApproachingLimit,
        IsOverBudget = dto.IsOverBudget,
        BaseCurrencyCode = dto.BaseCurrencyCode,
    };
}

public sealed class TopVendorPayload
{
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string TaxCode { get; init; } = string.Empty;
    public bool IsVerified { get; init; }
    public int DocumentCount { get; init; }
    public decimal TotalAmountInBaseCurrency { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;

    public static TopVendorPayload From(TopVendorDto dto) => new()
    {
        VendorId = dto.VendorId,
        VendorName = dto.VendorName,
        TaxCode = dto.TaxCode,
        IsVerified = dto.IsVerified,
        DocumentCount = dto.DocumentCount,
        TotalAmountInBaseCurrency = dto.TotalAmountInBaseCurrency,
        BaseCurrencyCode = dto.BaseCurrencyCode,
    };
}

public sealed class TopEmployeePayload
{
    public Guid MembershipId { get; init; }
    public Guid AccountId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public int ExpenseCount { get; init; }
    public decimal TotalAmountInBaseCurrency { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;

    public static TopEmployeePayload From(TopEmployeeDto dto) => new()
    {
        MembershipId = dto.MembershipId,
        AccountId = dto.AccountId,
        EmployeeName = dto.EmployeeName,
        DepartmentName = dto.DepartmentName,
        ExpenseCount = dto.ExpenseCount,
        TotalAmountInBaseCurrency = dto.TotalAmountInBaseCurrency,
        BaseCurrencyCode = dto.BaseCurrencyCode,
    };
}

public sealed class PendingPaymentItemPayload
{
    public Guid PaymentId { get; init; }
    public Guid DocumentId { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public decimal AmountInBaseCurrency { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public DateTime RecordedAt { get; init; }
    public int AgeDays { get; init; }

    public static PendingPaymentItemPayload From(PendingPaymentItemDto dto) => new()
    {
        PaymentId = dto.PaymentId,
        DocumentId = dto.DocumentId,
        Reference = dto.Reference,
        EmployeeName = dto.EmployeeName,
        DepartmentName = dto.DepartmentName,
        Amount = dto.Amount,
        CurrencyCode = dto.CurrencyCode,
        AmountInBaseCurrency = dto.AmountInBaseCurrency,
        BaseCurrencyCode = dto.BaseCurrencyCode,
        PaymentMethod = dto.PaymentMethod,
        RecordedAt = dto.RecordedAt,
        AgeDays = dto.AgeDays,
    };
}

public sealed class MonthlyTrendPointPayload
{
    public int Year { get; init; }
    public int Month { get; init; }
    public decimal ExpenseTotal { get; init; }
    public int DocumentCount { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;

    public static MonthlyTrendPointPayload From(MonthlyTrendPointDto dto) => new()
    {
        Year = dto.Year,
        Month = dto.Month,
        ExpenseTotal = dto.ExpenseTotal,
        DocumentCount = dto.DocumentCount,
        BaseCurrencyCode = dto.BaseCurrencyCode,
    };
}
