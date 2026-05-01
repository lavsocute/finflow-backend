using FinFlow.Domain.Abstractions;
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
        CurrencyCode currencyCode,
        decimal amountInVnd,
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
        AmountInVnd = amountInVnd;
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
    public CurrencyCode CurrencyCode { get; private set; }
    public decimal AmountInVnd { get; private set; }
    public int Month { get; private set; }
    public int Year { get; private set; }
    public DateTime ExpenseDate { get; private set; }
    public ExpenseStatus Status { get; private set; }
    public Guid CreatedByMembershipId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public static Expense Create(
        Guid idTenant,
        Guid idDepartment,
        Guid documentId,
        Guid paymentId,
        Guid idCategory,
        string vendorName,
        decimal amount,
        CurrencyCode currencyCode,
        decimal amountInVnd,
        int month,
        int year,
        DateTime expenseDate,
        Guid createdByMembershipId)
    {
        var now = DateTime.UtcNow;
        return new Expense(
            Guid.NewGuid(),
            idTenant,
            idDepartment,
            documentId,
            paymentId,
            idCategory,
            vendorName.Trim(),
            amount,
            currencyCode,
            amountInVnd,
            month,
            year,
            expenseDate,
            ExpenseStatus.Confirmed,
            createdByMembershipId,
            now,
            now);
    }

    public Result Reject(string reason)
    {
        if (Status != ExpenseStatus.Confirmed)
            return Result.Failure(ExpenseErrors.AlreadyProcessed);

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(ExpenseErrors.RejectionReasonRequired);

        Status = ExpenseStatus.Rejected;
        UpdatedAt = DateTime.UtcNow;

        return Result.Success();
    }
}

public enum ExpenseStatus
{
    Confirmed,
    Rejected
}