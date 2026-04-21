using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class TenantSubscriptionErrors
{
    public static readonly Error TenantRequired = new("TenantSubscription.TenantRequired", "Tenant is required.");
    public static readonly Error InvalidPeriod = new("TenantSubscription.InvalidPeriod", "Subscription period is invalid.");
    public static readonly Error InvalidPlanTier = new("TenantSubscription.InvalidPlanTier", "Plan tier is invalid.");
    public static readonly Error SamePlanTier = new("TenantSubscription.SamePlanTier", "The tenant subscription already uses this plan tier.");
    public static readonly Error SubscriptionNotFound = new("TenantSubscription.NotFound", "No active subscription found for the tenant.");
}
