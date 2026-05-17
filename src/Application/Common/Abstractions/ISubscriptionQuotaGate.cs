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

    /// <summary>
    /// Check if tenant + member still have token quota left.
    /// Returns failure if either has hit their token cap. Use BEFORE calling the LLM
    /// to short-circuit when quota is exhausted by previous calls.
    /// </summary>
    Task<Result> EnsureChatbotTokensAvailableAsync(
        Guid tenantId,
        Guid membershipId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Record actual token consumption after an LLM response is received.
    /// Should be called even if the response was filtered/redacted (cost is incurred regardless).
    /// </summary>
    Task RecordChatbotTokensAsync(
        Guid tenantId,
        Guid membershipId,
        long tokensUsed,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken cancellationToken);

    Task RecordOcrUsageAsync(
        SubscriptionQuotaDecision decision,
        CancellationToken cancellationToken);
}
