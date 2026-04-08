using FinFlow.Domain.Entities;
using FinFlow.Domain.Invitations;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class InvitationRepository : IInvitationRepository
{
    private readonly ApplicationDbContext _dbContext;

    public InvitationRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<bool> HasPendingInvitationAsync(string email, Guid idTenant, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        return await _dbContext.Set<Invitation>()
            .IgnoreQueryFilters()
            .AnyAsync(i =>
                i.Email == normalizedEmail &&
                i.IdTenant == idTenant &&
                i.IsActive &&
                !i.AcceptedAt.HasValue &&
                !i.RevokedAt.HasValue &&
                i.ExpiresAt > now,
                cancellationToken);
    }

    public async Task<InvitationSummary?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<Invitation>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(i => i.TokenHash == tokenHash)
            .Select(i => new InvitationSummary(i.Id, i.Email, i.IdTenant, i.InvitedByMembershipId, i.Role, i.ExpiresAt, i.AcceptedAt, i.RevokedAt, i.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<InvitationSummary>> GetPendingByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        return await _dbContext.Set<Invitation>()
            .AsNoTracking()
            .Where(i =>
                i.IdTenant == idTenant &&
                i.IsActive &&
                !i.AcceptedAt.HasValue &&
                !i.RevokedAt.HasValue &&
                i.ExpiresAt > now)
            .Select(i => new InvitationSummary(i.Id, i.Email, i.IdTenant, i.InvitedByMembershipId, i.Role, i.ExpiresAt, i.AcceptedAt, i.RevokedAt, i.IsActive))
            .OrderByDescending(i => i.ExpiresAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Invitation?> GetByTokenForUpdateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = Invitation.HashToken(rawToken);
        return await _dbContext.Set<Invitation>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);
    }

    public void Add(Invitation invitation) => _dbContext.Set<Invitation>().Add(invitation);
    public void Update(Invitation invitation) => _dbContext.Set<Invitation>().Update(invitation);
}
