using FinFlow.Domain.Audit;
using FinFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AuditLogRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(AuditLog log, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<AuditLog>().AddAsync(log, cancellationToken);
}
