using FinFlow.Application.Budgets.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;
using MediatR;

namespace FinFlow.Application.Budgets.Queries.CheckBudgetAvailable;

public sealed class CheckBudgetAvailableQueryHandler : IRequestHandler<CheckBudgetAvailableQuery, Result<BudgetCheckDto>>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public CheckBudgetAvailableQueryHandler(
        IBudgetRepository budgetRepository,
        IDepartmentRepository departmentRepository)
    {
        _budgetRepository = budgetRepository;
        _departmentRepository = departmentRepository;
    }

    public async Task<Result<BudgetCheckDto>> Handle(CheckBudgetAvailableQuery request, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetByIdAsync(request.DepartmentId, cancellationToken);
        if (department is null || department.IdTenant != request.TenantId)
            return Result.Failure<BudgetCheckDto>(DepartmentErrors.NotFound);

        var budget = await _budgetRepository.GetByDepartmentAndPeriodAsync(
            request.DepartmentId,
            request.Month,
            request.Year,
            cancellationToken);

        decimal allocatedAmount = 0;
        decimal spentAmount = 0;

        if (budget != null)
        {
            allocatedAmount = budget.AllocatedAmount;
            spentAmount = await _budgetRepository.CalculateSpentAmountAsync(
                request.DepartmentId, request.Month, request.Year, cancellationToken);
        }

        return Result.Success(new BudgetCheckDto(
            request.DepartmentId,
            request.Month,
            request.Year,
            allocatedAmount,
            spentAmount,
            allocatedAmount - spentAmount,
            spentAmount > allocatedAmount,
            allocatedAmount > 0 && spentAmount >= (allocatedAmount * 0.9m)));
    }
}