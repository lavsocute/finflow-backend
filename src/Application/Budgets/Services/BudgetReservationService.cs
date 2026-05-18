using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Budgets.Services;

internal sealed class BudgetReservationService : IBudgetReservationService
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly ILogger<BudgetReservationService> _logger;

    public BudgetReservationService(
        IBudgetRepository budgetRepository,
        ILogger<BudgetReservationService> logger)
    {
        _budgetRepository = budgetRepository;
        _logger = logger;
    }

    public Task<Result> CommitAsync(BudgetMovement movement, BudgetExceededTrigger trigger, CancellationToken ct) =>
        ApplyAsync(movement, ct, b =>
        {
            var r = b.ApplyCommitment(movement.AmountInBaseCurrency, trigger);
            if (r.IsFailure)
                _logger.LogWarning("Budget commit failed: {Error} (movement={Movement})", r.Error.Description, movement);
            return r;
        });

    public Task<Result> CommitWithOverrideAsync(
        BudgetMovement movement,
        BudgetExceededTrigger trigger,
        Guid overrodeByMembershipId,
        string justification,
        decimal overAmount,
        CancellationToken ct) =>
        ApplyAsync(movement, ct, b =>
        {
            var r = b.ApplyCommitment(movement.AmountInBaseCurrency, trigger);
            if (r.IsFailure)
                return r;
            // Override audit event raised on the Budget aggregate so the
            // SaveChanges pipeline picks it up alongside any threshold events.
            b.RecordOverrideUsed(
                overrodeByMembershipId,
                justification.Trim(),
                overAmount,
                movement.SourceEntityId,
                movement.SourceEntityType);
            return Result.Success();
        });

    public Task<Result> ReleaseCommitmentAsync(BudgetMovement movement, CancellationToken ct) =>
        ApplyAsync(movement, ct, b =>
        {
            var available = b.CommittedAmount;
            // Tolerate partial release when caller's amount exceeds tracked
            // commitment (vd: budget was created after the doc was approved).
            var amount = Math.Min(movement.AmountInBaseCurrency, available);
            if (amount <= 0)
                return Result.Success();
            return b.ReleaseCommitment(amount);
        });

    public Task<Result> ConvertCommitmentToSpentAsync(BudgetMovement movement, BudgetExceededTrigger trigger, CancellationToken ct) =>
        ApplyAsync(movement, ct, b =>
        {
            // If commitment was lost (vd: budget didn't exist at approve time),
            // fall through to plain ApplyExpenseReopen-style add to Spent.
            if (b.CommittedAmount >= movement.AmountInBaseCurrency)
                return b.ApplyConfirmation(movement.AmountInBaseCurrency, trigger);
            return b.ApplyExpenseReopen(movement.AmountInBaseCurrency, trigger);
        });

    public Task<Result> ReverseSpentAsync(BudgetMovement movement, CancellationToken ct) =>
        ApplyAsync(movement, ct, b =>
        {
            var amount = Math.Min(movement.AmountInBaseCurrency, b.SpentAmount);
            if (amount <= 0)
                return Result.Success();
            return b.ApplyExpenseRollback(amount);
        });

    public Task<Result> ReapplySpentAsync(BudgetMovement movement, BudgetExceededTrigger trigger, CancellationToken ct) =>
        ApplyAsync(movement, ct, b => b.ApplyExpenseReopen(movement.AmountInBaseCurrency, trigger));

    /// <summary>
    /// Common load + mutate + update flow. Returns success when no budget
    /// exists for the period (caller treats that as "untracked" — same
    /// semantics as before reservations existed).
    /// </summary>
    private async Task<Result> ApplyAsync(
        BudgetMovement movement,
        CancellationToken ct,
        Func<Budget, Result> mutator)
    {
        if (movement.AmountInBaseCurrency <= 0)
            return Result.Failure(BudgetErrors.InvalidAmount);
        if (movement.TenantId == Guid.Empty)
            return Result.Failure(BudgetErrors.TenantRequired);
        if (movement.DepartmentId == Guid.Empty)
            return Result.Failure(BudgetErrors.DepartmentRequired);

        var budget = await _budgetRepository.GetEntityByDepartmentAndPeriodAsync(
            movement.TenantId,
            movement.DepartmentId,
            movement.Month,
            movement.Year,
            ct);

        if (budget is null)
        {
            _logger.LogDebug(
                "No budget for tenant={TenantId} dept={DepartmentId} {Month}/{Year}; movement {SourceEntityType}#{SourceEntityId} skipped.",
                movement.TenantId, movement.DepartmentId, movement.Month, movement.Year,
                movement.SourceEntityType, movement.SourceEntityId);
            return Result.Success();
        }

        var result = mutator(budget);
        if (result.IsFailure)
            return result;

        _budgetRepository.Update(budget);
        return Result.Success();
    }
}
