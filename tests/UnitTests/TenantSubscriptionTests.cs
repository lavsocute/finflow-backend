using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.UnitTests;

public sealed class TenantSubscriptionTests
{
    [Fact]
    public void Create_Succeeds_WithValidPeriod()
    {
        var tenantId = Guid.NewGuid();
        var periodStart = new DateTime(2026, 04, 01, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc);

        var result = TenantSubscription.Create(
            tenantId,
            PlanTier.Pro,
            periodStart,
            periodEnd);

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, result.Value.IdTenant);
        Assert.Equal(PlanTier.Pro, result.Value.PlanTier);
        Assert.Equal(SubscriptionStatus.Active, result.Value.Status);
        Assert.Equal(periodStart, result.Value.PeriodStart);
        Assert.Equal(periodEnd, result.Value.PeriodEnd);
        Assert.Contains(SubscriptionFeature.DocumentReview, result.Value.Features);
        Assert.Contains(SubscriptionFeature.DocumentOcr, result.Value.Features);
    }

    [Fact]
    public void Create_Fails_WhenPeriodIsInvalid()
    {
        var tenantId = Guid.NewGuid();
        var periodStart = new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 04, 01, 0, 0, 0, DateTimeKind.Utc);

        var result = TenantSubscription.Create(
            tenantId,
            PlanTier.Pro,
            periodStart,
            periodEnd);

        Assert.True(result.IsFailure);
        Assert.Equal(TenantSubscriptionErrors.InvalidPeriod.Code, result.Error.Code);
    }

    [Fact]
    public void ChangePlanTier_Succeeds_WhenDowngradingToFree()
    {
        var tenantId = Guid.NewGuid();
        var periodStart = new DateTime(2026, 04, 01, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc);
        var subscription = TenantSubscription.Create(
            tenantId,
            PlanTier.Pro,
            periodStart,
            periodEnd).Value;

        var result = subscription.ChangePlanTier(PlanTier.Free);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanTier.Free, subscription.PlanTier);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Single(subscription.Features);
        Assert.Contains(SubscriptionFeature.DocumentUpload, subscription.Features);
        Assert.DoesNotContain(SubscriptionFeature.DocumentReview, subscription.Features);
        Assert.DoesNotContain(SubscriptionFeature.DocumentOcr, subscription.Features);
    }
}
