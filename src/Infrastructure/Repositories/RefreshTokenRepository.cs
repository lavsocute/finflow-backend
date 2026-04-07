using FinFlow.Domain.Entities;
using FinFlow.Domain.RefreshTokens;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _dbContext;

    public RefreshTokenRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var hashedToken = RefreshToken.HashToken(token);
        return await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(rt => rt.Token == hashedToken, cancellationToken);
    }

    public async Task<IReadOnlyList<RefreshToken>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<RefreshToken>()
            .AsNoTracking()
            .Where(rt => rt.AccountId == accountId && !rt.IsRevoked)
            .OrderByDescending(rt => rt.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(RefreshToken refreshToken) => _dbContext.Set<RefreshToken>().Add(refreshToken);
    public async Task<bool> RevokeByTokenAsync(string token, string reason, CancellationToken cancellationToken = default)
    {
        var hashedToken = RefreshToken.HashToken(token);
        var affected = await _dbContext.Set<RefreshToken>()
            .Where(rt => rt.Token == hashedToken && !rt.IsRevoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(rt => rt.IsRevoked, true)
                .SetProperty(rt => rt.ReasonRevoked, reason),
                cancellationToken);

        return affected > 0;
    }

    public void Update(RefreshToken refreshToken)
    {
        var entry = _dbContext.Entry(refreshToken);
        if (entry.State == EntityState.Detached)
            _dbContext.Set<RefreshToken>().Attach(refreshToken);
        entry.Property(x => x.IsRevoked).IsModified = true;
        entry.Property(x => x.ReasonRevoked).IsModified = true;
        entry.Property(x => x.ReplacedByToken).IsModified = true;
    }

    public async Task RevokeAllForAccountAsync(Guid accountId, string reason, CancellationToken cancellationToken = default)
    {
        var tokens = await _dbContext.Set<RefreshToken>()
            .Where(rt => rt.AccountId == accountId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.Revoke(reason);
        }
    }
}
