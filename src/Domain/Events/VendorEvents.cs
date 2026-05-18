using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Events;

/// <summary>
/// Raised when the document submission flow auto-creates a vendor record because
/// staff entered a tax code that doesn't yet exist in the tenant. The vendor is
/// always <c>IsVerified=false</c> on creation; this event lets audit + manager
/// dashboards flag it for review.
/// </summary>
public sealed record VendorAutoCreatedDomainEvent(
    Guid VendorId,
    Guid TenantId,
    string TaxCode,
    string Name,
    Guid CreatedByMembershipId,
    Guid SourceDocumentId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
