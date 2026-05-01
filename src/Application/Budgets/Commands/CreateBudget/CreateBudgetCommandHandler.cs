using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;

namespace FinFlow.Application.Budgets.Commands.CreateBudget;

public sealed class CreateBudgetCommandHandler : ICommandHandler<CreateBudgetCommand, Result<BudgetDetailDto>>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateBudgetCommandHandler(
        IBudgetRepository budgetRepository,
        IDepartmentRepository departmentRepository,
        IUnitOfWork unitOfWork)
    {
        _budgetRepository = budgetRepository;
        _departmentRepository = departmentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BudgetDetailDto>> Handle(CreateBudgetCommand request, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetByIdAsync(request.DepartmentId, cancellationToken);
        if (department is null || department.IdTenant != request.TenantId)
            return Result.Failure<BudgetDetailDto>(DepartmentErrors.NotFound);

        var exists = await _budgetRepository.ExistsAsync(request.DepartmentId, request.Month, request.Year, cancellationToken);
        if (exists)
            return Result.Failure<BudgetDetailDto>(BudgetErrors.DuplicateBudget);

        var createResult = Budget.Create(
            request.TenantId,
            request.DepartmentId,
            request.Month,
            request.Year,
            request.Amount);

        if (createResult.IsFailure)
            return Result.Failure<BudgetDetailDto>(createResult.Error);

        var budget = createResult.Value;
        _budgetRepository.Add(budget);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var spentAmount = await _budgetRepository.CalculateSpentAmountAsync(
            request.DepartmentId, request.Month, request.Year, cancellationToken);

        return Result.Success(new BudgetDetailDto(
            budget.Id,
            budget.IdDepartment,
            department.Name,
            budget.Month,
            budget.Year,
            budget.AllocatedAmount,
            spentAmount,
            budget.AllocatedAmount - spentAmount,
            spentAmount > budget.AllocatedAmount,
            budget.AllocatedAmount > 0 && spentAmount >= (budget.AllocatedAmount * 0.9m)));
    }
}