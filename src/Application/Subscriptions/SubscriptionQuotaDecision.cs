using FinFlow.Domain.Enums;

namespace FinFlow.Application.Subscriptions;

public sealed record SubscriptionQuotaDecision(
    Guid TenantId,
    Guid MembershipId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    SubscriptionFeature Feature,
    int ApprovedUnitCount,
    PlanEntitlements Entitlements,
    int WorkspaceOcrUsed,
    int MemberOcrUsed,
    int WorkspaceChatUsed,
    int MemberChatUsed);
