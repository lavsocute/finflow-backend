using FinFlow.Domain.Budgets;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Budgets.Services;

internal sealed class BudgetGuard : IBudgetGuard
{
    private const decimal WarningThreshold = 0.85m;

    private readonly IBudgetRepository _budgetRepository;

    public BudgetGuard(IBudgetRepository budgetRepository) => _budgetRepository = budgetRepository;

    public async Task<BudgetCheckResult> CheckAsync(
        Guid tenantId,
        Guid departmentId,
        int month,
        int year,
        decimal amountInBaseCurrency,
        CancellationToken ct)
    {
        var budget = await _budgetRepository.GetByDepartmentAndPeriodAsync(
            tenantId, departmentId, month, year, ct);

        // No budget configured → outside scope. Caller can still proceed (the
        // commit will no-op in the reservation service).
        if (budget is null)
            return new BudgetCheckResult(
                Outcome: BudgetCheckOutcome.Allowed,
                AvailableBefore: 0m,
                AvailableAfter: 0m,
                AllocatedAmount: 0m,
                CommittedAmount: 0m,
                SpentAmount: 0m,
                EnforcementMode: BudgetEnforcementMode.Off,
                BudgetExists: false);

        var carry = budget.CarryOverFromPreviousMonth ?? 0m;
        var pool = budget.AllocatedAmount + carry;
        var availableBefore = pool - budget.CommittedAmount - budget.SpentAmount;
        var availableAfter = availableBefore - amountInBaseCurrency;
        var willBeOver = availableAfter < 0m;

        var outcome = (willBeOver, budget.EnforcementMode) switch
        {
            (true, BudgetEnforcementMode.HardBlock) => BudgetCheckOutcome.BlockedByHardLimit,
            (true, BudgetEnforcementMode.SoftBlock) => BudgetCheckOutcome.RequiresOverride,
            (true, BudgetEnforcementMode.Off) => BudgetCheckOutcome.AllowedWithWarning,
            _ when pool > 0 && (budget.CommittedAmount + budget.SpentAmount + amountInBaseCurrency) >= (pool * WarningThreshold)
                => BudgetCheckOutcome.AllowedWithWarning,
            _ => BudgetCheckOutcome.Allowed
        };

        return new BudgetCheckResult(
            Outcome: outcome,
            AvailableBefore: availableBefore,
            AvailableAfter: availableAfter,
            AllocatedAmount: budget.AllocatedAmount,
            CommittedAmount: budget.CommittedAmount,
            SpentAmount: budget.SpentAmount,
            EnforcementMode: budget.EnforcementMode,
            BudgetExists: true);
    }
}
