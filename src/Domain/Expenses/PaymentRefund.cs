using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Expenses;

public enum PaymentRefundStatus
{
    Pending,
    Completed,
    Failed
}

public sealed class PaymentRefund : Entity, IMultiTenant
{
    private PaymentRefund(
        Guid id,
        Guid paymentId,
        Guid idTenant,
        decimal amount,
        string reason,
        Guid initiatedByMembershipId,
        DateTime initiatedAt,
        PaymentRefundStatus status,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        PaymentId = paymentId;
        IdTenant = idTenant;
        Amount = amount;
        Reason = reason;
        InitiatedByMembershipId = initiatedByMembershipId;
        InitiatedAt = initiatedAt;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    private PaymentRefund() { }

    public Guid PaymentId { get; private set; }
    public Guid IdTenant { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = null!;
    public Guid InitiatedByMembershipId { get; private set; }
    public DateTime InitiatedAt { get; private set; }
    public PaymentRefundStatus Status { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Concurrency token mapped to PostgreSQL xmin.
    /// </summary>
    public uint Version { get; private set; }

    public static Result<PaymentRefund> Create(
        Guid paymentId,
        Guid idTenant,
        decimal amount,
        string reason,
        Guid initiatedByMembershipId)
    {
        if (paymentId == Guid.Empty)
            return Result.Failure<PaymentRefund>(PaymentErrors.RefundPaymentIdRequired);
        if (idTenant == Guid.Empty)
            return Result.Failure<PaymentRefund>(PaymentErrors.TenantRequired);
        if (amount <= 0)
            return Result.Failure<PaymentRefund>(PaymentErrors.RefundAmountInvalid);
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<PaymentRefund>(PaymentErrors.RefundReasonRequired);
        if (initiatedByMembershipId == Guid.Empty)
            return Result.Failure<PaymentRefund>(PaymentErrors.RefundInitiatedByRequired);

        var trimmed = reason.Trim();
        if (trimmed.Length > 500)
            return Result.Failure<PaymentRefund>(PaymentErrors.RefundReasonTooLong);

        var now = DateTime.UtcNow;
        return Result.Success(new PaymentRefund(
            Guid.NewGuid(),
            paymentId,
            idTenant,
            amount,
            trimmed,
            initiatedByMembershipId,
            now,
            PaymentRefundStatus.Pending,
            now,
            now));
    }

    public Result MarkCompleted()
    {
        if (Status != PaymentRefundStatus.Pending)
            return Result.Failure(PaymentErrors.RefundNotPending);

        Status = PaymentRefundStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result MarkFailed(string failureReason)
    {
        if (Status != PaymentRefundStatus.Pending)
            return Result.Failure(PaymentErrors.RefundNotPending);
        if (string.IsNullOrWhiteSpace(failureReason))
            return Result.Failure(PaymentErrors.RefundFailureReasonRequired);

        Status = PaymentRefundStatus.Failed;
        FailureReason = failureReason.Trim();
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
