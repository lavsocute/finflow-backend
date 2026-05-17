using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Common.Audit;

/// <summary>
/// Maps a domain event to an <see cref="AuditLog"/> entry.
/// Returns <c>null</c> when the event is not auditable (e.g. unknown type).
/// </summary>
public interface IDomainEventAuditMapper
{
    AuditLog? Map(IDomainEvent domainEvent, Guid? tenantId, Guid? accountId);
}
