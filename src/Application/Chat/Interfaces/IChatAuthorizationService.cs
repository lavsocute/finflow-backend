using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Interfaces;

public interface IChatAuthorizationService
{
    Task<ChatAuthorizationProfile> GetAuthorizationProfileAsync(Guid membershipId, CancellationToken ct = default);
    Task<ChatAccessScope> GetChatAccessScopeAsync(Guid membershipId, CancellationToken ct = default);
}
