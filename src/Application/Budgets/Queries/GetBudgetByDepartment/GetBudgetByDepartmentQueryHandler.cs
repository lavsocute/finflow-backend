using FinFlow.Application.Budgets.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;
using MediatR;

namespace FinFlow.Application.Budgets.Queries.GetBudgetByDepartment;

public sealed class GetBudgetByDepartmentQueryHandler : IRequestHandler<GetBudgetByDepartmentQuery, Result<BudgetDetailDto?>>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public GetBudgetByDepartmentQueryHandler(
        IBudgetRepository budgetRepository,
        IDepartmentRepository departmentRepository)
    {
        _budgetRepository = budgetRepository;
        _departmentRepository = departmentRepository;
    }

    public async Task<Result<BudgetDetailDto?>> Handle(GetBudgetByDepartmentQuery request, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetByIdAsync(request.DepartmentId, cancellationToken);
        if (department is null || department.IdTenant != request.TenantId)
            return Result.Failure<BudgetDetailDto?>(DepartmentErrors.NotFound);

        var budget = await _budgetRepository.GetByDepartmentAndPeriodAsync(
            request.DepartmentId,
            request.Month,
            request.Year,
            cancellationToken);

        if (budget is null)
            return Result.Success<BudgetDetailDto?>(null);

        var spentAmount = await _budgetRepository.CalculateSpentAmountAsync(
            request.DepartmentId, request.Month, request.Year, cancellationToken);

        return Result.Success(new BudgetDetailDto(
            budget.Id,
            budget.IdDepartment,
            budget.DepartmentName,
            budget.Month,
            budget.Year,
            budget.AllocatedAmount,
            spentAmount,
            budget.AllocatedAmount - spentAmount,
            spentAmount > budget.AllocatedAmount,
            budget.AllocatedAmount > 0 && spentAmount >= (budget.AllocatedAmount * 0.9m)));
    }
}