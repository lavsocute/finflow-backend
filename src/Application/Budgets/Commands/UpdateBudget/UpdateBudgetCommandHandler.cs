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
        var budget = await _budgetRepository.GetByIdAsync(request.BudgetId, cancellationToken);
        if (budget is null || budget.IdTenant != request.TenantId)
            return Result.Failure<BudgetDetailDto>(BudgetErrors.NotFound);

        var entity = await _budgetRepository.GetEntityByIdAsync(request.BudgetId, cancellationToken);
        if (entity is null)
            return Result.Failure<BudgetDetailDto>(BudgetErrors.NotFound);

        var updateResult = entity.UpdateAmount(request.Amount);
        if (updateResult.IsFailure)
            return Result.Failure<BudgetDetailDto>(updateResult.Error);

        var spentAmount = await _budgetRepository.CalculateSpentAmountAsync(
            entity.IdDepartment, entity.Month, entity.Year, cancellationToken);

        entity.RecalculateSpent(spentAmount);

        _budgetRepository.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new BudgetDetailDto(
            entity.Id,
            entity.IdDepartment,
            budget.DepartmentName,
            entity.Month,
            entity.Year,
            entity.AllocatedAmount,
            spentAmount,
            entity.AllocatedAmount - spentAmount,
            spentAmount > entity.AllocatedAmount,
            entity.AllocatedAmount > 0 && spentAmount >= (entity.AllocatedAmount * 0.9m)));
    }
}