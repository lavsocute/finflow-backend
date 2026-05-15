using FinFlow.Application.Subscriptions;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Common.Abstractions;

public interface ISubscriptionQuotaGate
{
    Task<Result<SubscriptionQuotaDecision>> EnsureChatbotAllowedAsync(
        Guid tenantId,
        Guid membershipId,
        int messageCount,
        CancellationToken cancellationToken);

    Task<Result<SubscriptionQuotaDecision>> EnsureOcrAllowedAsync(
        Guid tenantId,
        Guid membershipId,
        int pageCount,
        CancellationToken cancellationToken);

    Task RecordChatbotUsageAsync(
        SubscriptionQuotaDecision decision,
        CancellationToken cancellationToken);

    Task RecordOcrUsageAsync(
        SubscriptionQuotaDecision decision,
        CancellationToken cancellationToken);
}
