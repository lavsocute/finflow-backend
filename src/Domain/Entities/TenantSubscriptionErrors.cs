using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Entities;

public static class TenantSubscriptionErrors
{
    public static readonly Error TenantRequired = new("TenantSubscription.TenantRequired", "Tenant is required.");
    public static readonly Error InvalidPeriod = new("TenantSubscription.InvalidPeriod", "Subscription period is invalid.");
    public static readonly Error InvalidPlanTier = new("TenantSubscription.InvalidPlanTier", "Plan tier is invalid.");
    public static readonly Error InvalidBillingCycle = new("TenantSubscription.InvalidBillingCycle", "Billing cycle months must be 1 (monthly) or 12 (annual).");
    public static readonly Error InvalidGracePeriod = new("TenantSubscription.InvalidGracePeriod", "Grace period days must be between 0 and 30.");
    public static readonly Error SamePlanTier = new("TenantSubscription.SamePlanTier", "The tenant subscription already uses this plan tier.");
    public static readonly Error SubscriptionNotFound = new("TenantSubscription.NotFound", "No active subscription found for the tenant.");
    public static readonly Error AlreadyCanceled = new("TenantSubscription.AlreadyCanceled", "Subscription is already canceled.");
    public static readonly Error AlreadyPaused = new("TenantSubscription.AlreadyPaused", "Subscription is already paused.");
    public static readonly Error NotPaused = new("TenantSubscription.NotPaused", "Subscription is not paused.");
    public static readonly Error CannotReactivate = new("TenantSubscription.CannotReactivate", "Subscription cannot be reactivated from current status.");
}
