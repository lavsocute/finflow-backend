using System.ComponentModel.DataAnnotations.Schema;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class TenantSubscription : Entity, IMultiTenant
{
    private TenantSubscription(
        Guid id,
        Guid idTenant,
        PlanTier planTier,
        DateTime periodStart,
        DateTime periodEnd,
        SubscriptionStatus status,
        List<SubscriptionFeature> features)
    {
        Id = id;
        IdTenant = idTenant;
        PlanTier = planTier;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Status = status;
        Features = features;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private TenantSubscription() { }

    public Guid IdTenant { get; private set; }

    [NotMapped]
    public Guid TenantId => IdTenant;

    public PlanTier PlanTier { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTime PeriodStart { get; private set; }
    public DateTime PeriodEnd { get; private set; }
    public List<SubscriptionFeature> Features { get; private set; } = [];
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public static Result<TenantSubscription> Create(
        Guid idTenant,
        PlanTier planTier,
        DateTime periodStart,
        DateTime periodEnd)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<TenantSubscription>(TenantSubscriptionErrors.TenantRequired);

        if (periodStart.Kind != DateTimeKind.Utc || periodEnd.Kind != DateTimeKind.Utc || periodStart >= periodEnd)
            return Result.Failure<TenantSubscription>(TenantSubscriptionErrors.InvalidPeriod);

        var features = GetFeatures(planTier);
        return Result.Success(new TenantSubscription(
            Guid.NewGuid(),
            idTenant,
            planTier,
            periodStart,
            periodEnd,
            SubscriptionStatus.Active,
            features));
    }

    public Result ChangePlanTier(PlanTier planTier)
    {
        if (PlanTier == planTier)
            return Result.Failure(TenantSubscriptionErrors.SamePlanTier);

        PlanTier = planTier;
        Features = GetFeatures(planTier);
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    private static List<SubscriptionFeature> GetFeatures(PlanTier planTier) =>
        planTier switch
        {
            PlanTier.Free => [SubscriptionFeature.DocumentUpload],
            PlanTier.Pro or PlanTier.Enterprise => [SubscriptionFeature.DocumentUpload, SubscriptionFeature.DocumentReview, SubscriptionFeature.DocumentOcr],
            _ => throw new ArgumentOutOfRangeException(nameof(planTier), planTier, null)
        };
}
