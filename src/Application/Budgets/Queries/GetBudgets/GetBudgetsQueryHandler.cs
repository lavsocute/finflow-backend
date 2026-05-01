using FinFlow.Application.Budgets.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Departments;
using FinFlow.Domain.Entities;
using MediatR;

namespace FinFlow.Application.Budgets.Queries.GetBudgets;

public sealed class GetBudgetsQueryHandler : IRequestHandler<GetBudgetsQuery, Result<IReadOnlyList<BudgetSummaryDto>>>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public GetBudgetsQueryHandler(
        IBudgetRepository budgetRepository,
        IDepartmentRepository departmentRepository)
    {
        _budgetRepository = budgetRepository;
        _departmentRepository = departmentRepository;
    }

    public async Task<Result<IReadOnlyList<BudgetSummaryDto>>> Handle(GetBudgetsQuery request, CancellationToken cancellationToken)
    {
        if (request.DepartmentId.HasValue)
        {
            var department = await _departmentRepository.GetByIdAsync(request.DepartmentId.Value, cancellationToken);
            if (department is null || department.IdTenant != request.TenantId)
                return Result.Failure<IReadOnlyList<BudgetSummaryDto>>(DepartmentErrors.NotFound);
        }

        var budgets = await _budgetRepository.GetByTenantIdAsync(
            request.TenantId,
            request.Month,
            request.Year,
            request.DepartmentId,
            cancellationToken);

        var responses = budgets
            .Select(b => new BudgetSummaryDto(
                b.Id,
                b.IdDepartment,
                b.DepartmentName,
                b.Month,
                b.Year,
                b.AllocatedAmount,
                b.SpentAmount,
                b.AllocatedAmount - b.SpentAmount))
            .ToList();

        return Result.Success<IReadOnlyList<BudgetSummaryDto>>(responses);
    }
}