using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Budgets.Commands.CarryOverBudgets;

public sealed class CarryOverBudgetsCommandHandler : ICommandHandler<CarryOverBudgetsCommand, Result<int>>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CarryOverBudgetsCommandHandler(IBudgetRepository budgetRepository, IUnitOfWork unitOfWork)
    {
        _budgetRepository = budgetRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(CarryOverBudgetsCommand request, CancellationToken cancellationToken)
    {
        if (request.FromMonth is < 1 or > 12) return Result.Failure<int>(BudgetErrors.InvalidMonth);
        if (request.ToMonth is < 1 or > 12) return Result.Failure<int>(BudgetErrors.InvalidMonth);
        if (request.CarryOverPercentage is < 0m or > 100m)
            return Result.Failure<int>(BudgetErrors.InvalidAmount);

        var sourceBudgets = await _budgetRepository.GetByTenantIdAsync(
            request.TenantId, request.FromMonth, request.FromYear, departmentId: null, cancellationToken);
        if (sourceBudgets.Count == 0)
            return Result.Success(0);

        var processed = 0;
        foreach (var src in sourceBudgets)
        {
            // Skip if a target budget already exists — admins manage that
            // manually so we don't overwrite intentional changes.
            var existing = await _budgetRepository.ExistsAsync(
                request.TenantId, src.IdDepartment, request.ToMonth, request.ToYear, cancellationToken);
            if (existing) continue;

            var srcEntity = await _budgetRepository.GetEntityByIdAsync(src.Id, request.TenantId, cancellationToken);
            if (srcEntity is null) continue;

            // Carry-over computed from previous month's actual remaining pool.
            // When previous month went over budget the carry-over is clamped to 0
            // — we don't carry debt forward.
            var pool = srcEntity.AllocatedAmount + (srcEntity.CarryOverFromPreviousMonth ?? 0m);
            var unused = Math.Max(0m, pool - srcEntity.CommittedAmount - srcEntity.SpentAmount);
            var carry = decimal.Round(unused * (request.CarryOverPercentage / 100m), 2, MidpointRounding.AwayFromZero);

            var targetResult = Budget.Create(
                request.TenantId,
                src.IdDepartment,
                request.ToMonth,
                request.ToYear,
                allocatedAmount: srcEntity.AllocatedAmount,
                baseCurrencyCode: srcEntity.BaseCurrencyCode,
                enforcementMode: srcEntity.EnforcementMode,
                carryOverFromPreviousMonth: carry > 0m ? carry : null);
            if (targetResult.IsFailure)
                return Result.Failure<int>(targetResult.Error);

            _budgetRepository.Add(targetResult.Value);
            processed++;
        }

        if (processed > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(processed);
    }
}
