using FinFlow.Application.Subscriptions;
using FinFlow.Domain.Enums;

namespace FinFlow.Infrastructure.Subscriptions;

public sealed class PlanEntitlementCatalog
{
    public PlanEntitlements GetFor(PlanTier planTier) =>
        planTier switch
        {
            PlanTier.Free => new PlanEntitlements(true, false, false, 1L * 1024 * 1024 * 1024, 0, 0),
            PlanTier.Pro => new PlanEntitlements(true, true, true, 10L * 1024 * 1024 * 1024, 1_000, 10_000),
            PlanTier.Enterprise => new PlanEntitlements(true, true, true, 100L * 1024 * 1024 * 1024, 10_000, 100_000),
            _ => throw new ArgumentOutOfRangeException(nameof(planTier), planTier, null)
        };
}
