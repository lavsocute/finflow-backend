namespace FinFlow.Application.Common.Abstractions;

public interface ILoginRateLimiter
{
    Task<bool> IsBlockedAsync(string? ip, string email, Guid? tenantId = null);
    Task RecordFailureAsync(string? ip, string email, Guid? tenantId = null);
    Task ResetAccountAsync(string email, Guid? tenantId = null);
}
