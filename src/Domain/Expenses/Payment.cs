using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Common;
using FinFlow.Domain.Events;
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
        string currencyCode,
        decimal exchangeRate,
        decimal amountInBaseCurrency,
        string baseCurrencyCode,
        Guid recordedByMembershipId,
        DateTime recordedAt,
        PaymentMethod method,
        PaymentStatus status,
        Guid? confirmedByMembershipId,
        DateTime? confirmedAt,
        string? executionReference,
        Guid? rejectedByMembershipId,
        DateTime? rejectedAt,
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
        AmountInBaseCurrency = amountInBaseCurrency;
        BaseCurrencyCode = baseCurrencyCode;
        RecordedByMembershipId = recordedByMembershipId;
        RecordedAt = recordedAt;
        Method = method;
        Status = status;
        ConfirmedByMembershipId = confirmedByMembershipId;
        ConfirmedAt = confirmedAt;
        ExecutionReference = executionReference;
        RejectedByMembershipId = rejectedByMembershipId;
        RejectedAt = rejectedAt;
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
    public string CurrencyCode { get; private set; } = null!;
    public decimal ExchangeRate { get; private set; }
    public decimal AmountInBaseCurrency { get; private set; }
    public string BaseCurrencyCode { get; private set; } = null!;
    public Guid RecordedByMembershipId { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public Guid? ConfirmedByMembershipId { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public string? ExecutionReference { get; private set; }
    public Guid? RejectedByMembershipId { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public PaymentRejectType? RejectionType { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid? CancelledByMembershipId { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Concurrency token mapped to PostgreSQL xmin.
    /// </summary>
    public uint Version { get; private set; }

    public static Result<Payment> Create(
        Guid idTenant,
        Guid documentId,
        Guid idDepartment,
        decimal amount,
        string currencyCode,
        decimal exchangeRate,
        string baseCurrencyCode,
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
        if (exchangeRate <= 0)
            return Result.Failure<Payment>(PaymentErrors.InvalidExchangeRate);
        if (recordedByMembershipId == Guid.Empty)
            return Result.Failure<Payment>(PaymentErrors.RecordedByRequired);

        var currencyResult = Currency.Create(currencyCode);
        if (currencyResult.IsFailure)
            return Result.Failure<Payment>(currencyResult.Error);

        var baseCurrencyResult = Currency.Create(baseCurrencyCode);
        if (baseCurrencyResult.IsFailure)
            return Result.Failure<Payment>(baseCurrencyResult.Error);

        // When document and base currency match, exchange rate must be 1.0 to keep data consistent.
        if (currencyResult.Value.Code == baseCurrencyResult.Value.Code && exchangeRate != 1m)
            return Result.Failure<Payment>(PaymentErrors.SameCurrencyRequiresUnitRate);

        var now = DateTime.UtcNow;
        var amountInBase = decimal.Round(amount * exchangeRate, 2, MidpointRounding.AwayFromZero);

        var payment = new Payment(
            Guid.NewGuid(),
            idTenant,
            documentId,
            idDepartment,
            amount,
            currencyResult.Value.Code,
            exchangeRate,
            amountInBase,
            baseCurrencyResult.Value.Code,
            recordedByMembershipId,
            now,
            method,
            PaymentStatus.Pending,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            notes?.Trim(),
            now,
            now);

        payment.RaiseDomainEvent(new PaymentRecordedDomainEvent(
            payment.Id,
            payment.IdTenant,
            payment.DocumentId,
            payment.RecordedByMembershipId,
            payment.Amount,
            payment.CurrencyCode,
            payment.Method));

        return Result.Success(payment);
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

        RaiseDomainEvent(new PaymentConfirmedDomainEvent(
            Id, IdTenant, confirmedByMembershipId, ExecutionReference, Amount, CurrencyCode));
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
        RejectedByMembershipId = rejectedByMembershipId;
        RejectedAt = DateTime.UtcNow;
        RejectionType = rejectionType;
        RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new PaymentRejectedDomainEvent(
            Id, IdTenant, rejectedByMembershipId, rejectionType, RejectionReason));
        return Result.Success();
    }

    private static readonly HashSet<PaymentMethod> SupportedMethods =
    [
        PaymentMethod.Cash,
        PaymentMethod.BankTransfer,
        PaymentMethod.Check,
        PaymentMethod.CreditCard,
        PaymentMethod.Payroll,
        PaymentMethod.Other
    ];

    public Result Update(PaymentMethod method, string? notes, Guid byMembershipId)
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(PaymentErrors.UpdateRequiresPending);
        if (byMembershipId == Guid.Empty)
            return Result.Failure(PaymentErrors.UpdatedByRequired);
        if (!SupportedMethods.Contains(method))
            return Result.Failure(PaymentErrors.InvalidPaymentMethod);

        var oldMethod = Method;
        var oldNotes = Notes;

        Method = method;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new PaymentUpdatedDomainEvent(
            Id, IdTenant, byMembershipId, oldMethod, method, oldNotes, Notes));
        return Result.Success();
    }

    public Result Cancel(string reason, Guid byMembershipId)
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(PaymentErrors.CancelRequiresPending);
        if (byMembershipId == Guid.Empty)
            return Result.Failure(PaymentErrors.CancelledByRequired);
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(PaymentErrors.CancellationReasonRequired);

        var trimmed = reason.Trim();
        if (trimmed.Length > 500)
            return Result.Failure(PaymentErrors.CancellationReasonTooLong);

        Status = PaymentStatus.Cancelled;
        CancelledByMembershipId = byMembershipId;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = trimmed;
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(new PaymentCancelledDomainEvent(Id, IdTenant, byMembershipId, trimmed));
        return Result.Success();
    }

    public Result<PaymentRefund> InitiateRefund(decimal amount, string reason, Guid byMembershipId)
    {
        if (Status != PaymentStatus.Confirmed)
            return Result.Failure<PaymentRefund>(PaymentErrors.RefundRequiresConfirmed);
        if (amount <= 0 || amount > Amount)
            return Result.Failure<PaymentRefund>(PaymentErrors.RefundAmountInvalid);

        var refundResult = PaymentRefund.Create(Id, IdTenant, amount, reason, byMembershipId);
        if (refundResult.IsFailure)
            return refundResult;

        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new PaymentRefundedDomainEvent(
            Id, IdTenant, byMembershipId, amount, refundResult.Value.Reason));
        return refundResult;
    }
}
