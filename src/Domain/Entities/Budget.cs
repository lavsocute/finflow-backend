using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Events;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class Budget : Entity, IMultiTenant, ISoftDeletable
{
    /// <summary>
    /// Threshold under which the budget emits a soft warning. The first time
    /// utilization (committed + spent) crosses 85% an event fires; second
    /// crossing at 95% fires a more urgent warning. We track which thresholds
    /// have already been signaled to avoid duplicate notifications.
    /// </summary>
    private const decimal WarningThresholdLow = 0.85m;
    private const decimal WarningThresholdHigh = 0.95m;

    private Budget(
        Guid id,
        Guid idTenant,
        Guid idDepartment,
        int month,
        int year,
        decimal allocatedAmount,
        decimal committedAmount,
        decimal spentAmount,
        decimal? carryOverFromPreviousMonth,
        string baseCurrencyCode,
        BudgetEnforcementMode enforcementMode,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        IdTenant = idTenant;
        IdDepartment = idDepartment;
        Month = month;
        Year = year;
        AllocatedAmount = allocatedAmount;
        CommittedAmount = committedAmount;
        SpentAmount = spentAmount;
        CarryOverFromPreviousMonth = carryOverFromPreviousMonth;
        BaseCurrencyCode = baseCurrencyCode;
        EnforcementMode = enforcementMode;
        IsActive = true;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    private Budget() { }

    public Guid IdTenant { get; private set; }
    public Guid IdDepartment { get; private set; }
    public int Month { get; private set; }
    public int Year { get; private set; }
    public decimal AllocatedAmount { get; private set; }
    public decimal CommittedAmount { get; private set; }
    public decimal SpentAmount { get; private set; }
    public decimal? CarryOverFromPreviousMonth { get; private set; }
    public string BaseCurrencyCode { get; private set; } = null!;
    public BudgetEnforcementMode EnforcementMode { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Concurrency token mapped to PostgreSQL xmin system column.
    /// Prevents lost updates when multiple lifecycle events apply concurrently.
    /// </summary>
    public uint Version { get; private set; }

    /// <summary>Total available pool = allocation + carry-over.</summary>
    public decimal TotalPool => AllocatedAmount + (CarryOverFromPreviousMonth ?? 0m);

    /// <summary>Outstanding capacity for new commitments.</summary>
    public decimal AvailableAmount => TotalPool - CommittedAmount - SpentAmount;

    public bool IsOverCommitted => CommittedAmount + SpentAmount > TotalPool;
    public bool IsOverSpent => SpentAmount > TotalPool;
    public bool IsNearLimit => TotalPool > 0 && (CommittedAmount + SpentAmount) >= (TotalPool * WarningThresholdLow);

    public static Result<Budget> Create(
        Guid idTenant,
        Guid idDepartment,
        int month,
        int year,
        decimal allocatedAmount,
        string baseCurrencyCode,
        BudgetEnforcementMode enforcementMode = BudgetEnforcementMode.SoftBlock,
        decimal? carryOverFromPreviousMonth = null)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<Budget>(BudgetErrors.TenantRequired);
        if (idDepartment == Guid.Empty)
            return Result.Failure<Budget>(BudgetErrors.DepartmentRequired);
        if (month < 1 || month > 12)
            return Result.Failure<Budget>(BudgetErrors.InvalidMonth);
        if (year < 2000 || year > 2100)
            return Result.Failure<Budget>(BudgetErrors.InvalidYear);
        if (allocatedAmount < 0)
            return Result.Failure<Budget>(BudgetErrors.InvalidAmount);
        if (carryOverFromPreviousMonth is < 0)
            return Result.Failure<Budget>(BudgetErrors.InvalidAmount);

        var normalizedCurrency = (baseCurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3)
            return Result.Failure<Budget>(BudgetErrors.CurrencyRequired);
        if (!Enum.IsDefined(enforcementMode))
            return Result.Failure<Budget>(BudgetErrors.InvalidEnforcementMode);

        var now = DateTime.UtcNow;
        var budget = new Budget(
            Guid.NewGuid(),
            idTenant,
            idDepartment,
            month,
            year,
            allocatedAmount,
            committedAmount: 0m,
            spentAmount: 0m,
            carryOverFromPreviousMonth,
            normalizedCurrency,
            enforcementMode,
            createdAt: now,
            updatedAt: now);

        budget.RaiseDomainEvent(new BudgetCreatedDomainEvent(
            budget.Id, budget.IdTenant, budget.IdDepartment,
            budget.Month, budget.Year, budget.AllocatedAmount));

        return Result.Success(budget);
    }

    public Result UpdateAmount(decimal amount)
    {
        if (amount < 0)
            return Result.Failure(BudgetErrors.InvalidAmount);

        if (AllocatedAmount == amount)
            return Result.Success();

        AllocatedAmount = amount;
        UpdatedAt = DateTime.UtcNow;

        // Single event per user action — coalesces with any RecalculateSpent
        // since reservation pipeline now drives spent updates separately.
        RaiseDomainEvent(new BudgetUpdatedDomainEvent(
            Id, IdTenant, IdDepartment, AllocatedAmount, SpentAmount));
        return Result.Success();
    }

    public Result ChangeEnforcementMode(BudgetEnforcementMode mode)
    {
        if (!Enum.IsDefined(mode))
            return Result.Failure(BudgetErrors.InvalidEnforcementMode);
        if (EnforcementMode == mode)
            return Result.Success();
        EnforcementMode = mode;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result UpdateCarryOver(decimal? amount)
    {
        if (amount is < 0)
            return Result.Failure(BudgetErrors.InvalidAmount);
        CarryOverFromPreviousMonth = amount;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Reserve <paramref name="amount"/> in the budget for an upcoming payment
    /// (e.g. when manager approves a reviewed document). Raises warning /
    /// exceeded events when thresholds are crossed.
    /// </summary>
    public Result ApplyCommitment(decimal amount, BudgetExceededTrigger trigger)
    {
        if (amount <= 0)
            return Result.Failure(BudgetErrors.InvalidAmount);

        var (preLow, preHigh, preOver) = GetThresholdState();
        CommittedAmount += amount;
        UpdatedAt = DateTime.UtcNow;

        EmitThresholdEvents(preLow, preHigh, preOver, trigger);
        return Result.Success();
    }

    /// <summary>
    /// Release a previously-committed amount (vd: reject doc, cancel payment).
    /// Floored at zero to tolerate replay/idempotency edge cases.
    /// </summary>
    public Result ReleaseCommitment(decimal amount)
    {
        if (amount <= 0)
            return Result.Failure(BudgetErrors.InvalidAmount);
        if (amount > CommittedAmount)
            return Result.Failure(BudgetErrors.InsufficientCommitment);

        CommittedAmount -= amount;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Move <paramref name="amount"/> from Committed to Spent (payment confirmed).
    /// Single in-memory mutation — does NOT re-query the DB (fixes B-1 bug
    /// where the SUM was stale by one expense).
    /// </summary>
    public Result ApplyConfirmation(decimal amount, BudgetExceededTrigger trigger)
    {
        if (amount <= 0)
            return Result.Failure(BudgetErrors.InvalidAmount);
        if (amount > CommittedAmount)
            return Result.Failure(BudgetErrors.InsufficientCommitment);

        var (preLow, preHigh, preOver) = GetThresholdState();
        CommittedAmount -= amount;
        SpentAmount += amount;
        UpdatedAt = DateTime.UtcNow;

        EmitThresholdEvents(preLow, preHigh, preOver, trigger);
        return Result.Success();
    }

    /// <summary>Refund / partial refund — reduce spent.</summary>
    public Result ApplyRefund(decimal amount)
    {
        if (amount <= 0)
            return Result.Failure(BudgetErrors.InvalidAmount);
        if (amount > SpentAmount)
            return Result.Failure(BudgetErrors.InsufficientSpent);

        SpentAmount -= amount;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Expense rejected after confirmation — reverse it from Spent.</summary>
    public Result ApplyExpenseRollback(decimal amount)
    {
        if (amount <= 0)
            return Result.Failure(BudgetErrors.InvalidAmount);
        if (amount > SpentAmount)
            return Result.Failure(BudgetErrors.InsufficientSpent);

        SpentAmount -= amount;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Expense reopened — re-add to Spent.</summary>
    public Result ApplyExpenseReopen(decimal amount, BudgetExceededTrigger trigger)
    {
        if (amount <= 0)
            return Result.Failure(BudgetErrors.InvalidAmount);

        var (preLow, preHigh, preOver) = GetThresholdState();
        SpentAmount += amount;
        UpdatedAt = DateTime.UtcNow;
        EmitThresholdEvents(preLow, preHigh, preOver, trigger);
        return Result.Success();
    }

    public Result Archive()
    {
        if (!IsActive)
            return Result.Failure(BudgetErrors.AlreadyArchived);
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Legacy helper for callers that recompute the entire spent total
    /// (vd: data migrations). Avoid in lifecycle pipelines.
    /// </summary>
    public void OverwriteSpent(decimal spentAmount)
    {
        if (spentAmount < 0)
            spentAmount = 0;
        SpentAmount = spentAmount;
        UpdatedAt = DateTime.UtcNow;
    }

    private (bool nearLow, bool nearHigh, bool over) GetThresholdState()
    {
        if (TotalPool <= 0)
            return (false, false, false);
        var utilization = (CommittedAmount + SpentAmount) / TotalPool;
        return (utilization >= WarningThresholdLow,
                utilization >= WarningThresholdHigh,
                utilization > 1m);
    }

    private void EmitThresholdEvents(bool wasLow, bool wasHigh, bool wasOver, BudgetExceededTrigger trigger)
    {
        var (nowLow, nowHigh, nowOver) = GetThresholdState();

        if (!wasLow && nowLow)
            RaiseDomainEvent(new BudgetWarningThresholdReachedDomainEvent(
                Id, IdTenant, IdDepartment,
                Math.Round(((CommittedAmount + SpentAmount) / TotalPool) * 100m, 2, MidpointRounding.AwayFromZero),
                85m));
        if (!wasHigh && nowHigh)
            RaiseDomainEvent(new BudgetWarningThresholdReachedDomainEvent(
                Id, IdTenant, IdDepartment,
                Math.Round(((CommittedAmount + SpentAmount) / TotalPool) * 100m, 2, MidpointRounding.AwayFromZero),
                95m));
        if (!wasOver && nowOver)
            RaiseDomainEvent(new BudgetExceededDomainEvent(
                Id, IdTenant, IdDepartment,
                Month, Year,
                AllocatedAmount, CommittedAmount, SpentAmount,
                OverAmount: (CommittedAmount + SpentAmount) - TotalPool,
                Trigger: trigger));
    }
}