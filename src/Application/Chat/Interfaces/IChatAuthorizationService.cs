using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Interfaces;

public interface IChatAuthorizationService
{
    Task<ChatAccessScope> GetChatAccessScopeAsync(Guid membershipId, CancellationToken ct = default);
}