using System.ComponentModel.DataAnnotations.Schema;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class TenantSubscription : Entity, IMultiTenant
{
    public const int DefaultGracePeriodDays = 3;
    public const int MonthlyBillingCycle = 1;
    public const int AnnualBillingCycle = 12;

    private TenantSubscription(
        Guid id,
        Guid idTenant,
        PlanTier planTier,
        DateTime periodStart,
        DateTime periodEnd,
        SubscriptionStatus status,
        int billingCycleMonths,
        int gracePeriodDays,
        List<SubscriptionFeature> features)
    {
        Id = id;
        IdTenant = idTenant;
        PlanTier = planTier;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Status = status;
        BillingCycleMonths = billingCycleMonths;
        GracePeriodDays = gracePeriodDays;
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

    /// <summary>Anchor date — billing periods are computed from this.</summary>
    public DateTime PeriodStart { get; private set; }

    /// <summary>End of the current billing window (PeriodStart + BillingCycleMonths).</summary>
    public DateTime PeriodEnd { get; private set; }

    /// <summary>1 = monthly, 12 = annual.</summary>
    public int BillingCycleMonths { get; private set; }

    /// <summary>Days of grace after PeriodEnd before subscription expires.</summary>
    public int GracePeriodDays { get; private set; }

    public List<SubscriptionFeature> Features { get; private set; } = [];
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? CanceledAt { get; private set; }
    public DateTime? PausedAt { get; private set; }

    /// <summary>Concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public static Result<TenantSubscription> Create(
        Guid idTenant,
        PlanTier planTier,
        DateTime periodStart,
        DateTime periodEnd,
        int billingCycleMonths = MonthlyBillingCycle,
        int gracePeriodDays = DefaultGracePeriodDays)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<TenantSubscription>(TenantSubscriptionErrors.TenantRequired);

        if (periodStart.Kind != DateTimeKind.Utc || periodEnd.Kind != DateTimeKind.Utc || periodStart >= periodEnd)
            return Result.Failure<TenantSubscription>(TenantSubscriptionErrors.InvalidPeriod);

        if (billingCycleMonths is not (MonthlyBillingCycle or AnnualBillingCycle))
            return Result.Failure<TenantSubscription>(TenantSubscriptionErrors.InvalidBillingCycle);

        if (gracePeriodDays is < 0 or > 30)
            return Result.Failure<TenantSubscription>(TenantSubscriptionErrors.InvalidGracePeriod);

        var features = GetFeatures(planTier);
        return Result.Success(new TenantSubscription(
            Guid.NewGuid(),
            idTenant,
            planTier,
            periodStart,
            periodEnd,
            SubscriptionStatus.Active,
            billingCycleMonths,
            gracePeriodDays,
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

    public Result StartNewCycle(PlanTier planTier, DateTime periodStart)
    {
        if (periodStart.Kind != DateTimeKind.Utc)
            return Result.Failure(TenantSubscriptionErrors.InvalidPeriod);

        PlanTier = planTier;
        Features = GetFeatures(planTier);
        PeriodStart = periodStart;
        PeriodEnd = periodStart.AddMonths(BillingCycleMonths);
        Status = SubscriptionStatus.Active;
        CanceledAt = null;
        PausedAt = null;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Mark subscription as past-due when billing period ends without renewal.</summary>
    public Result MarkPastDue()
    {
        if (Status != SubscriptionStatus.Active)
            return Result.Success(); // Idempotent — already past-due/expired/etc.

        Status = SubscriptionStatus.PastDue;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Mark subscription as expired (grace period ended).</summary>
    public Result MarkExpired()
    {
        if (Status == SubscriptionStatus.Expired)
            return Result.Success();
        if (Status == SubscriptionStatus.Canceled)
            return Result.Failure(TenantSubscriptionErrors.AlreadyCanceled);

        Status = SubscriptionStatus.Expired;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Reactivate a past-due/expired subscription (e.g., payment succeeded). Renews period.</summary>
    public Result Reactivate(DateTime newPeriodStart)
    {
        if (Status != SubscriptionStatus.PastDue && Status != SubscriptionStatus.Expired)
            return Result.Failure(TenantSubscriptionErrors.CannotReactivate);

        if (newPeriodStart.Kind != DateTimeKind.Utc)
            return Result.Failure(TenantSubscriptionErrors.InvalidPeriod);

        PeriodStart = newPeriodStart;
        PeriodEnd = newPeriodStart.AddMonths(BillingCycleMonths);
        Status = SubscriptionStatus.Active;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Cancel by user. Subscription remains usable until PeriodEnd, then becomes Expired.</summary>
    public Result Cancel()
    {
        if (Status == SubscriptionStatus.Canceled)
            return Result.Failure(TenantSubscriptionErrors.AlreadyCanceled);

        Status = SubscriptionStatus.Canceled;
        CanceledAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Pause by admin (e.g., support hold). No quota, can resume later.</summary>
    public Result Pause()
    {
        if (Status == SubscriptionStatus.Paused)
            return Result.Failure(TenantSubscriptionErrors.AlreadyPaused);

        Status = SubscriptionStatus.Paused;
        PausedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Resume from paused state.</summary>
    public Result Resume()
    {
        if (Status != SubscriptionStatus.Paused)
            return Result.Failure(TenantSubscriptionErrors.NotPaused);

        Status = SubscriptionStatus.Active;
        PausedAt = null;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Computes the effective status at a given time using lazy renewal logic.
    /// Returns the status the subscription SHOULD have based on dates, without mutating.
    /// </summary>
    public SubscriptionStatus ComputeEffectiveStatus(DateTime utcNow)
    {
        if (Status is SubscriptionStatus.Paused or SubscriptionStatus.Canceled)
            return Status;

        var graceCutoff = PeriodEnd.AddDays(GracePeriodDays);

        if (utcNow <= PeriodEnd)
            return SubscriptionStatus.Active;

        if (utcNow <= graceCutoff)
            return SubscriptionStatus.PastDue;

        return SubscriptionStatus.Expired;
    }

    /// <summary>
    /// Computes the current billing window for a given time using the anchor (PeriodStart).
    /// Returns (start, end) of the period that contains utcNow.
    /// For lazy renewal — does not mutate the entity.
    /// </summary>
    public (DateTime Start, DateTime End) ComputeCurrentPeriod(DateTime utcNow)
    {
        if (utcNow <= PeriodEnd)
            return (PeriodStart, PeriodEnd);

        // Compute how many full cycles have elapsed since PeriodStart.
        var anchor = PeriodStart;
        var cycleStart = anchor;
        var cycleEnd = anchor.AddMonths(BillingCycleMonths);

        while (cycleEnd <= utcNow)
        {
            cycleStart = cycleEnd;
            cycleEnd = cycleStart.AddMonths(BillingCycleMonths);
        }

        return (cycleStart, cycleEnd);
    }

    private static List<SubscriptionFeature> GetFeatures(PlanTier planTier) =>
        planTier switch
        {
            PlanTier.Free => [SubscriptionFeature.DocumentUpload],
            PlanTier.Pro or PlanTier.Enterprise => [SubscriptionFeature.DocumentUpload, SubscriptionFeature.DocumentReview, SubscriptionFeature.DocumentOcr],
            _ => throw new ArgumentOutOfRangeException(nameof(planTier), planTier, null)
        };
}
