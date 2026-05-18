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
            request.TenantId,
            request.DepartmentId,
            request.Month,
            request.Year,
            cancellationToken);

        if (budget is null)
            return Result.Success<BudgetDetailDto?>(null);

        // Use the snapshot stored on Budget entity — committed/spent are now
        // maintained in-memory by the lifecycle pipeline (no aggregation needed).
        var available = budget.AllocatedAmount + (budget.CarryOverFromPreviousMonth ?? 0m)
                         - budget.CommittedAmount - budget.SpentAmount;
        var pool = budget.AllocatedAmount + (budget.CarryOverFromPreviousMonth ?? 0m);
        var isOver = budget.SpentAmount > pool;
        var isNearLimit = pool > 0 && (budget.CommittedAmount + budget.SpentAmount) >= (pool * 0.85m);

        return Result.Success<BudgetDetailDto?>(new BudgetDetailDto(
            budget.Id,
            budget.IdDepartment,
            budget.DepartmentName,
            budget.Month,
            budget.Year,
            budget.AllocatedAmount,
            budget.SpentAmount,
            available,
            isOver,
            isNearLimit));
    }
}