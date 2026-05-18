using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Budgets.Services;

/// <summary>
/// Coordinates budget movements driven by lifecycle events (approve doc,
/// record payment, confirm payment, refund, reject expense...). Each method
/// loads the Budget entity for the (tenant, department, month, year) tuple
/// and applies a state transition via the Budget entity's helper methods —
/// no DB-level SUM is performed (that bug B-1 is gone).
///
/// All methods participate in the caller's UnitOfWork — they call
/// _budgetRepository.Update but do NOT SaveChanges. Caller batches the
/// SaveChanges so business + budget mutations are atomic.
///
/// Idempotency: state-machine prevention upstream. Approve fires once per
/// document, Confirm once per payment, etc. We don't dedup at this layer.
///
/// Concurrency: xmin token on Budget detects concurrent updates and EF Core
/// throws DbUpdateConcurrencyException, which bubbles to the caller's retry
/// pipeline (e.g. MediatR retry behavior, or simple re-try in the handler).
/// </summary>
public interface IBudgetReservationService
{
    /// <summary>
    /// Reserve <paramref name="movement"/> against the budget. Used by approve
    /// document, record payment. No-op when no budget exists for the period.
    /// </summary>
    Task<Result> CommitAsync(BudgetMovement movement, BudgetExceededTrigger trigger, CancellationToken ct);

    /// <summary>
    /// Variant of <see cref="CommitAsync"/> for the over-budget approval path.
    /// Performs the same commit + emits <c>BudgetOverrideUsedDomainEvent</c>
    /// for audit / notification.
    /// </summary>
    Task<Result> CommitWithOverrideAsync(
        BudgetMovement movement,
        BudgetExceededTrigger trigger,
        Guid overrodeByMembershipId,
        string justification,
        decimal overAmount,
        CancellationToken ct);

    /// <summary>
    /// Release a previously committed amount. Used by reject doc, cancel payment.
    /// </summary>
    Task<Result> ReleaseCommitmentAsync(BudgetMovement movement, CancellationToken ct);

    /// <summary>
    /// Move from Committed to Spent. Used by confirm payment (the canonical
    /// "money left the bank" event).
    /// </summary>
    Task<Result> ConvertCommitmentToSpentAsync(BudgetMovement movement, BudgetExceededTrigger trigger, CancellationToken ct);

    /// <summary>
    /// Reduce Spent. Used by refund payment, reject confirmed expense.
    /// </summary>
    Task<Result> ReverseSpentAsync(BudgetMovement movement, CancellationToken ct);

    /// <summary>
    /// Re-add to Spent. Used by reopen expense.
    /// </summary>
    Task<Result> ReapplySpentAsync(BudgetMovement movement, BudgetExceededTrigger trigger, CancellationToken ct);
}

public sealed record BudgetMovement(
    Guid TenantId,
    Guid DepartmentId,
    int Month,
    int Year,
    decimal AmountInBaseCurrency,
    Guid SourceEntityId,
    string SourceEntityType,
    string Reason);
