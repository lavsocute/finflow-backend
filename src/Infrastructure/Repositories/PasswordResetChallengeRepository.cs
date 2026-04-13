using FinFlow.Domain.Entities;
using FinFlow.Domain.PasswordResetChallenges;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Repositories;

internal sealed class PasswordResetChallengeRepository : IPasswordResetChallengeRepository
{
    private readonly ApplicationDbContext _dbContext;

    public PasswordResetChallengeRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<PasswordResetChallenge?> GetLatestByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        _dbContext.Set<PasswordResetChallenge>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PasswordResetChallenge?> GetLatestByAccountIdForUpdateAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        _dbContext.Set<PasswordResetChallenge>()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PasswordResetChallenge?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        _dbContext.Set<PasswordResetChallenge>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

    public Task<PasswordResetChallenge?> GetByTokenHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        _dbContext.Set<PasswordResetChallenge>()
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

    public void Add(PasswordResetChallenge challenge) => _dbContext.Set<PasswordResetChallenge>().Add(challenge);

    public void Update(PasswordResetChallenge challenge) => _dbContext.Set<PasswordResetChallenge>().Update(challenge);
}
