namespace FinFlow.Domain.Enums;

public enum SubscriptionStatus
{
    /// <summary>Subscription is active and within the billing period.</summary>
    Active = 0,

    /// <summary>
    /// Billing period ended without renewal. User is in grace period — can still
    /// access read-only features but quota-consuming features (OCR, Chat) are blocked.
    /// </summary>
    PastDue = 1,

    /// <summary>
    /// Grace period ended. Effectively downgraded to Free tier entitlements.
    /// Data is preserved; user can re-subscribe at any time.
    /// </summary>
    Expired = 2,

    /// <summary>User cancelled the subscription. Treated like Expired but explicit user intent.</summary>
    Canceled = 3,

    /// <summary>Admin paused the subscription temporarily (e.g., support hold). No billing, no quota.</summary>
    Paused = 4
}
