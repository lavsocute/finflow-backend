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
            request.TenantId,
            request.DepartmentId,
            request.Month,
            request.Year,
            cancellationToken);

        decimal allocatedAmount = 0;
        decimal committedAmount = 0;
        decimal spentAmount = 0;
        decimal carryOver = 0;

        if (budget != null)
        {
            allocatedAmount = budget.AllocatedAmount;
            committedAmount = budget.CommittedAmount;
            spentAmount = budget.SpentAmount;
            carryOver = budget.CarryOverFromPreviousMonth ?? 0m;
        }
        else
        {
            // No budget row → still compute spent from confirmed expenses so
            // callers checking "any allocation?" can distinguish "0 allocated, 0 spent"
            // from "0 allocated, X spent" (latter means real overspend).
            spentAmount = await _budgetRepository.CalculateSpentAmountAsync(
                request.TenantId, request.DepartmentId, request.Month, request.Year, cancellationToken);
        }

        var pool = allocatedAmount + carryOver;
        var available = pool - committedAmount - spentAmount;
        var isOver = (committedAmount + spentAmount) > pool;
        var isNearLimit = pool > 0 && (committedAmount + spentAmount) >= (pool * 0.85m);

        return Result.Success(new BudgetCheckDto(
            request.DepartmentId,
            request.Month,
            request.Year,
            allocatedAmount,
            spentAmount,
            available,
            isOver,
            isNearLimit));
    }
}