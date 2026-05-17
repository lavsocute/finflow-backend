namespace FinFlow.Application.Payments.DTOs;

public sealed record PaymentRefundResponse(
    Guid Id,
    Guid PaymentId,
    decimal Amount,
    string Reason,
    string Status,
    Guid InitiatedByMembershipId,
    DateTime InitiatedAt);
