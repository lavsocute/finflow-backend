using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Expenses;

namespace FinFlow.Domain.Events;

public sealed record PaymentRecordedDomainEvent(
    Guid PaymentId,
    Guid TenantId,
    Guid DocumentId,
    Guid RecordedByMembershipId,
    decimal Amount,
    string CurrencyCode,
    PaymentMethod Method) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record PaymentConfirmedDomainEvent(
    Guid PaymentId,
    Guid TenantId,
    Guid ConfirmedByMembershipId,
    string? ExecutionReference,
    decimal Amount,
    string CurrencyCode) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record PaymentRejectedDomainEvent(
    Guid PaymentId,
    Guid TenantId,
    Guid RejectedByMembershipId,
    PaymentRejectType RejectionType,
    string? Reason) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record PaymentUpdatedDomainEvent(
    Guid PaymentId,
    Guid TenantId,
    Guid UpdatedByMembershipId,
    PaymentMethod OldMethod,
    PaymentMethod NewMethod,
    string? OldNotes,
    string? NewNotes) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record PaymentCancelledDomainEvent(
    Guid PaymentId,
    Guid TenantId,
    Guid CancelledByMembershipId,
    string Reason) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record PaymentRefundedDomainEvent(
    Guid PaymentId,
    Guid TenantId,
    Guid InitiatedByMembershipId,
    decimal RefundAmount,
    string Reason) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
