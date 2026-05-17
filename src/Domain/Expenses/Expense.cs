using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Common;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Expenses;

public sealed class Expense : Entity, IMultiTenant
{
    private Expense(
        Guid id,
        Guid idTenant,
        Guid idDepartment,
        Guid documentId,
        Guid paymentId,
        Guid idCategory,
        string vendorName,
        decimal amount,
        string currencyCode,
        decimal amountInBaseCurrency,
        string baseCurrencyCode,
        int month,
        int year,
        DateTime expenseDate,
        ExpenseStatus status,
        Guid createdByMembershipId,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        IdTenant = idTenant;
        IdDepartment = idDepartment;
        DocumentId = documentId;
        PaymentId = paymentId;
        IdCategory = idCategory;
        VendorName = vendorName;
        Amount = amount;
        CurrencyCode = currencyCode;
        AmountInBaseCurrency = amountInBaseCurrency;
        BaseCurrencyCode = baseCurrencyCode;
        Month = month;
        Year = year;
        ExpenseDate = expenseDate;
        Status = status;
        CreatedByMembershipId = createdByMembershipId;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    private Expense() { }

    public Guid IdTenant { get; private set; }
    public Guid IdDepartment { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid IdCategory { get; private set; }
    public string VendorName { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public decimal AmountInBaseCurrency { get; private set; }
    public string BaseCurrencyCode { get; private set; } = null!;
    public int Month { get; private set; }
    public int Year { get; private set; }
    public DateTime ExpenseDate { get; private set; }
    public ExpenseStatus Status { get; private set; }
    public Guid CreatedByMembershipId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public Guid? RejectedByMembershipId { get; private set; }
    public DateTime? ReopenedAt { get; private set; }
    public string? ReopenedReason { get; private set; }
    public Guid? ReopenedByMembershipId { get; private set; }

    /// <summary>
    /// Concurrency token mapped to PostgreSQL xmin.
    /// </summary>
    public uint Version { get; private set; }

    public const int DefaultReopenWindowDays = 30;

    public static Result<Expense> Create(
        Guid idTenant,
        Guid idDepartment,
        Guid documentId,
        Guid paymentId,
        Guid idCategory,
        string vendorName,
        decimal amount,
        string currencyCode,
        decimal amountInBaseCurrency,
        string baseCurrencyCode,
        int month,
        int year,
        DateTime expenseDate,
        Guid createdByMembershipId)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<Expense>(ExpenseErrors.TenantRequired);
        if (idDepartment == Guid.Empty)
            return Result.Failure<Expense>(ExpenseErrors.DepartmentRequired);
        if (idCategory == Guid.Empty)
            return Result.Failure<Expense>(ExpenseErrors.CategoryRequired);
        if (amount <= 0)
            return Result.Failure<Expense>(ExpenseErrors.InvalidAmount);
        if (month is < 1 or > 12)
            return Result.Failure<Expense>(ExpenseErrors.InvalidMonth);
        if (year < 2000 || year > 2100)
            return Result.Failure<Expense>(ExpenseErrors.InvalidYear);

        var currencyResult = Currency.Create(currencyCode);
        if (currencyResult.IsFailure)
            return Result.Failure<Expense>(currencyResult.Error);

        var baseCurrencyResult = Currency.Create(baseCurrencyCode);
        if (baseCurrencyResult.IsFailure)
            return Result.Failure<Expense>(baseCurrencyResult.Error);

        var now = DateTime.UtcNow;
        return Result.Success(new Expense(
            Guid.NewGuid(),
            idTenant,
            idDepartment,
            documentId,
            paymentId,
            idCategory,
            string.IsNullOrWhiteSpace(vendorName) ? "Unknown" : vendorName.Trim(),
            amount,
            currencyResult.Value.Code,
            amountInBaseCurrency,
            baseCurrencyResult.Value.Code,
            month,
            year,
            expenseDate,
            ExpenseStatus.Confirmed,
            createdByMembershipId,
            now,
            now));
    }

    public Result Reject(string reason, Guid? rejectedByMembershipId = null)
    {
        if (Status != ExpenseStatus.Confirmed)
            return Result.Failure(ExpenseErrors.AlreadyProcessed);

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(ExpenseErrors.RejectionReasonRequired);

        var trimmed = reason.Trim();
        Status = ExpenseStatus.Rejected;
        RejectionReason = trimmed;
        RejectedAt = DateTime.UtcNow;
        RejectedByMembershipId = rejectedByMembershipId;
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new ExpenseRejectedDomainEvent(Id, IdTenant, rejectedByMembershipId, trimmed));
        return Result.Success();
    }

    public Result Reopen(string reason, Guid byMembershipId, int reopenWindowDays = DefaultReopenWindowDays)
    {
        if (Status != ExpenseStatus.Rejected)
            return Result.Failure(ExpenseErrors.NotRejected);

        if (RejectedAt is null)
            return Result.Failure(ExpenseErrors.ReopenWindowExpired);

        if ((DateTime.UtcNow - RejectedAt.Value).TotalDays > reopenWindowDays)
            return Result.Failure(ExpenseErrors.ReopenWindowExpired);

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(ExpenseErrors.ReopenReasonRequired);

        var trimmed = reason.Trim();
        if (trimmed.Length > 500)
            return Result.Failure(ExpenseErrors.ReopenReasonTooLong);

        if (byMembershipId == Guid.Empty)
            return Result.Failure(ExpenseErrors.ReopenedByRequired);

        Status = ExpenseStatus.Confirmed;
        RejectionReason = null;
        ReopenedAt = DateTime.UtcNow;
        ReopenedReason = trimmed;
        ReopenedByMembershipId = byMembershipId;
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new ExpenseReopenedDomainEvent(Id, IdTenant, byMembershipId, trimmed));
        return Result.Success();
    }
}

public enum ExpenseStatus
{
    Confirmed,
    Rejected
}