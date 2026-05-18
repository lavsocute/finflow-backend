using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Events;

/// <summary>
/// Raised when a freshly-submitted ReviewedDocument matches the dedup hash of
/// existing non-rejected documents within the sliding window. Soft-warning —
/// the submit still succeeds. Listeners should notify accountants/managers
/// for human review.
/// </summary>
public sealed record DuplicateReceiptFlaggedDomainEvent(
    Guid DocumentId,
    Guid TenantId,
    string DedupHash,
    Guid SubmitterMembershipId,
    IReadOnlyList<Guid> ConflictingDocumentIds) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
