namespace FinFlow.Domain.RefreshTokens;

public record RefreshTokenSummary(Guid Id, Guid AccountId, DateTime ExpiresAt, bool IsRevoked, bool IsActive);

public interface IRefreshTokenRepository
{
    // Read Methods (DTO)
    Task<RefreshTokenSummary?> GetSummaryByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefreshTokenSummary>> GetSummariesByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);

    // Write Methods (Entity tracked)
    Task<FinFlow.Domain.Entities.RefreshToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> RevokeByTokenAsync(string token, string reason, CancellationToken cancellationToken = default);
    void Add(FinFlow.Domain.Entities.RefreshToken refreshToken);
    void Update(FinFlow.Domain.Entities.RefreshToken refreshToken);
    Task RevokeAllForAccountAsync(Guid accountId, string reason, CancellationToken cancellationToken = default);
}
