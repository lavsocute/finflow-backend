using FinFlow.Domain.Entities;
using FinFlow.Domain.RefreshTokens;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _dbContext;

    public RefreshTokenRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    // Read Method: Returns DTO via Select projection
    public async Task<RefreshTokenSummary?> GetSummaryByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var hashedToken = RefreshToken.HashToken(token);
        return await _dbContext.Set<RefreshToken>()
            .AsNoTracking()
            .Where(rt => rt.Token == hashedToken)
            .Select(rt => new RefreshTokenSummary(rt.Id, rt.AccountId, rt.ExpiresAt, rt.IsRevoked, rt.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }

    // Read Method: Returns List of DTOs
    public async Task<IReadOnlyList<RefreshTokenSummary>> GetSummariesByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        await _dbContext.Set<RefreshToken>()
            .AsNoTracking()
            .Where(rt => rt.AccountId == accountId && !rt.IsRevoked)
            .OrderByDescending(rt => rt.CreatedAt)
            .Select(rt => new RefreshTokenSummary(rt.Id, rt.AccountId, rt.ExpiresAt, rt.IsRevoked, rt.IsActive))
            .ToListAsync(cancellationToken);

    // Write Method: Returns tracked Entity for updates
    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var hashedToken = RefreshToken.HashToken(token);
        return await _dbContext.Set<RefreshToken>()
            .FirstOrDefaultAsync(rt => rt.Token == hashedToken, cancellationToken);
    }

    // Write Method: Revoke via ExecuteUpdate
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

    public void Add(RefreshToken refreshToken) => _dbContext.Set<RefreshToken>().Add(refreshToken);
    public void Update(RefreshToken refreshToken) => _dbContext.Set<RefreshToken>().Update(refreshToken);

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
