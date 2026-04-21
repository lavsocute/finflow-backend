using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Application.Subscriptions;

namespace FinFlow.Application.Common.Abstractions;

public interface ISubscriptionFeatureGate
{
    Task<PlanEntitlements> GetEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result> EnsureFeatureEnabledAsync(Guid tenantId, SubscriptionFeature feature, CancellationToken cancellationToken);
    Task<Result> EnsureOcrAllowedAsync(Guid tenantId, int pageCount, CancellationToken cancellationToken);
    Task<Result> EnsureChatbotAllowedAsync(Guid tenantId, int messageCount, CancellationToken cancellationToken);
}
