using FinFlow.Domain.Entities;

namespace FinFlow.Domain.RefreshTokens;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefreshToken>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<bool> RevokeByTokenAsync(string token, string reason, CancellationToken cancellationToken = default);
    void Add(RefreshToken refreshToken);
    void Update(RefreshToken refreshToken);
    Task RevokeAllForAccountAsync(Guid accountId, string reason, CancellationToken cancellationToken = default);
}
