using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Expenses;

public sealed class Payment : Entity, IMultiTenant
{
    private Payment(
        Guid id,
        Guid idTenant,
        Guid documentId,
        Guid idDepartment,
        decimal amount,
        CurrencyCode currencyCode,
        decimal exchangeRate,
        decimal amountInVnd,
        Guid recordedByMembershipId,
        DateTime recordedAt,
        PaymentMethod method,
        PaymentStatus status,
        Guid? confirmedByMembershipId,
        DateTime? confirmedAt,
        string? executionReference,
        PaymentRejectType? rejectionType,
        string? rejectionReason,
        string? notes,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        IdTenant = idTenant;
        DocumentId = documentId;
        IdDepartment = idDepartment;
        Amount = amount;
        CurrencyCode = currencyCode;
        ExchangeRate = exchangeRate;
        AmountInVnd = amountInVnd;
        RecordedByMembershipId = recordedByMembershipId;
        RecordedAt = recordedAt;
        Method = method;
        Status = status;
        ConfirmedByMembershipId = confirmedByMembershipId;
        ConfirmedAt = confirmedAt;
        ExecutionReference = executionReference;
        RejectionType = rejectionType;
        RejectionReason = rejectionReason;
        Notes = notes;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    private Payment() { }

    public Guid IdTenant { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid IdDepartment { get; private set; }
    public decimal Amount { get; private set; }
    public CurrencyCode CurrencyCode { get; private set; }
    public decimal ExchangeRate { get; private set; }
    public decimal AmountInVnd { get; private set; }
    public Guid RecordedByMembershipId { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public Guid? ConfirmedByMembershipId { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public string? ExecutionReference { get; private set; }
    public PaymentRejectType? RejectionType { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public static Result<Payment> Create(
        Guid idTenant,
        Guid documentId,
        Guid idDepartment,
        decimal amount,
        CurrencyCode currencyCode,
        decimal exchangeRate,
        Guid recordedByMembershipId,
        PaymentMethod method,
        string? notes)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<Payment>(PaymentErrors.TenantRequired);
        if (documentId == Guid.Empty)
            return Result.Failure<Payment>(PaymentErrors.DocumentIdRequired);
        if (idDepartment == Guid.Empty)
            return Result.Failure<Payment>(PaymentErrors.DepartmentRequired);
        if (amount <= 0)
            return Result.Failure<Payment>(PaymentErrors.InvalidAmount);
        if (recordedByMembershipId == Guid.Empty)
            return Result.Failure<Payment>(PaymentErrors.RecordedByRequired);

        var now = DateTime.UtcNow;
        var amountInVnd = amount * exchangeRate;

        return Result.Success(new Payment(
            Guid.NewGuid(),
            idTenant,
            documentId,
            idDepartment,
            amount,
            currencyCode,
            exchangeRate,
            amountInVnd,
            recordedByMembershipId,
            now,
            method,
            PaymentStatus.Pending,
            null,
            null,
            null,
            null,
            null,
            notes?.Trim(),
            now,
            now));
    }

    public Result Confirm(Guid confirmedByMembershipId, string? executionReference)
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(PaymentErrors.AlreadyProcessed);
        if (confirmedByMembershipId == Guid.Empty)
            return Result.Failure(PaymentErrors.ConfirmedByRequired);

        Status = PaymentStatus.Confirmed;
        ConfirmedByMembershipId = confirmedByMembershipId;
        ConfirmedAt = DateTime.UtcNow;
        ExecutionReference = string.IsNullOrWhiteSpace(executionReference)
            ? null
            : executionReference.Trim();
        UpdatedAt = DateTime.UtcNow;

        return Result.Success();
    }

    public Result Reject(Guid rejectedByMembershipId, PaymentRejectType rejectionType, string? reason)
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(PaymentErrors.AlreadyProcessed);
        if (rejectedByMembershipId == Guid.Empty)
            return Result.Failure(PaymentErrors.RejectedByRequired);
        if (!Enum.IsDefined(rejectionType))
            return Result.Failure(PaymentErrors.RejectionTypeRequired);
        if (rejectionType == PaymentRejectType.Other && string.IsNullOrWhiteSpace(reason))
            return Result.Failure(PaymentErrors.RejectionReasonRequired);

        Status = PaymentStatus.Rejected;
        ConfirmedByMembershipId = rejectedByMembershipId;
        ConfirmedAt = DateTime.UtcNow;
        RejectionType = rejectionType;
        RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = DateTime.UtcNow;

        return Result.Success();
    }
}
