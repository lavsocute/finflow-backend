using FinFlow.Domain.Entities;

namespace FinFlow.Domain.PasswordResetChallenges;

public interface IPasswordResetChallengeRepository
{
    Task<PasswordResetChallenge?> GetLatestByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<PasswordResetChallenge?> GetLatestByAccountIdForUpdateAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<PasswordResetChallenge?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<PasswordResetChallenge?> GetByTokenHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default);
    void Add(PasswordResetChallenge challenge);
    void Update(PasswordResetChallenge challenge);
}
