using FinFlow.Domain.Entities;

namespace FinFlow.Domain.Audit;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken cancellationToken = default);
}
