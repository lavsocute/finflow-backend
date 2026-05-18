using FinFlow.Domain.Enums;

namespace FinFlow.Application.Budgets.Services;

/// <summary>
/// Policy gate: before approving a document or recording a payment we ask the
/// guard whether the budget can absorb the new commitment. The guard does NOT
/// mutate state — that's the reservation service's job. Caller (handler)
/// decides what to do with the verdict.
/// </summary>
public interface IBudgetGuard
{
    Task<BudgetCheckResult> CheckAsync(
        Guid tenantId,
        Guid departmentId,
        int month,
        int year,
        decimal amountInBaseCurrency,
        CancellationToken ct);
}

public sealed record BudgetCheckResult(
    BudgetCheckOutcome Outcome,
    decimal AvailableBefore,
    decimal AvailableAfter,
    decimal AllocatedAmount,
    decimal CommittedAmount,
    decimal SpentAmount,
    BudgetEnforcementMode EnforcementMode,
    bool BudgetExists)
{
    public bool RequiresOverride => Outcome == BudgetCheckOutcome.RequiresOverride;
    public bool IsBlocked => Outcome == BudgetCheckOutcome.BlockedByHardLimit;
}

public enum BudgetCheckOutcome
{
    /// <summary>Within budget. Caller may proceed without ceremony.</summary>
    Allowed = 0,

    /// <summary>Within budget but utilization would cross 85% threshold.</summary>
    AllowedWithWarning = 1,

    /// <summary>EnforcementMode=SoftBlock + over-budget. Manager must supply justification.</summary>
    RequiresOverride = 2,

    /// <summary>EnforcementMode=HardBlock + over-budget. Reject outright.</summary>
    BlockedByHardLimit = 3
}
