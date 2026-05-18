using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Budgets.Commands.UpdateBudget;

public sealed class UpdateBudgetCommandHandler : ICommandHandler<UpdateBudgetCommand, Result<BudgetDetailDto>>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateBudgetCommandHandler(
        IBudgetRepository budgetRepository,
        IUnitOfWork unitOfWork)
    {
        _budgetRepository = budgetRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BudgetDetailDto>> Handle(UpdateBudgetCommand request, CancellationToken cancellationToken)
    {
        var entity = await _budgetRepository.GetEntityByIdAsync(request.BudgetId, request.TenantId, cancellationToken);
        if (entity is null)
            return Result.Failure<BudgetDetailDto>(BudgetErrors.NotFound);

        var summary = await _budgetRepository.GetByIdAsync(request.BudgetId, request.TenantId, cancellationToken);
        if (summary is null)
            return Result.Failure<BudgetDetailDto>(BudgetErrors.NotFound);

        var updateResult = entity.UpdateAmount(request.Amount);
        if (updateResult.IsFailure)
            return Result.Failure<BudgetDetailDto>(updateResult.Error);

        // No more double-event: UpdateAmount alone raises BudgetUpdated.
        // Spent + Committed are maintained by the lifecycle pipeline; we don't
        // recompute them here.
        _budgetRepository.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var pool = entity.AllocatedAmount + (entity.CarryOverFromPreviousMonth ?? 0m);
        var available = pool - entity.CommittedAmount - entity.SpentAmount;

        return Result.Success(new BudgetDetailDto(
            entity.Id,
            entity.IdDepartment,
            summary.DepartmentName,
            entity.Month,
            entity.Year,
            entity.AllocatedAmount,
            entity.SpentAmount,
            available,
            entity.IsOverSpent,
            entity.IsNearLimit));
    }
}
