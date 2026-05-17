using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Events;

public sealed record ReviewedDocumentWithdrawnDomainEvent(
    Guid DocumentId,
    Guid TenantId,
    Guid MembershipId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record ReviewedDocumentApprovedDomainEvent(
    Guid DocumentId,
    Guid TenantId,
    Guid? ApprovedByMembershipId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record ReviewedDocumentRejectedDomainEvent(
    Guid DocumentId,
    Guid TenantId,
    Guid? RejectedByMembershipId,
    string Reason) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record UploadedDocumentDraftDeletedDomainEvent(
    Guid DraftId,
    Guid TenantId,
    Guid MembershipId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
