namespace FinFlow.Application.Chat.Interfaces;

public interface IRateLimitService
{
    Task<bool> CheckUserRateLimitAsync(Guid membershipId, CancellationToken ct = default);
    Task<bool> CheckTenantRateLimitAsync(Guid tenantId, CancellationToken ct = default);
}