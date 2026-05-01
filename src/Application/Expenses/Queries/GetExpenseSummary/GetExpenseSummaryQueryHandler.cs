using FinFlow.Application.Expenses.DTOs;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Budgets;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Expenses.Queries.GetExpenseSummary;

internal sealed class GetExpenseSummaryQueryHandler : IRequestHandler<GetExpenseSummaryQuery, Result<ExpenseSummaryDto>>
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IBudgetRepository _budgetRepository;

    public GetExpenseSummaryQueryHandler(
        IExpenseRepository expenseRepository,
        IBudgetRepository budgetRepository)
    {
        _expenseRepository = expenseRepository;
        _budgetRepository = budgetRepository;
    }

    public async Task<Result<ExpenseSummaryDto>> Handle(GetExpenseSummaryQuery request, CancellationToken cancellationToken)
    {
        var totalSpent = await _expenseRepository.GetTotalSpentByDepartmentAndPeriodAsync(
            request.DepartmentId,
            request.Month,
            request.Year,
            ExpenseStatus.Confirmed,
            cancellationToken);

        var budget = await _budgetRepository.GetByDepartmentAndPeriodAsync(
            request.DepartmentId,
            request.Month,
            request.Year,
            cancellationToken);

        var allocated = budget?.AllocatedAmount ?? 0m;
        var remaining = allocated - totalSpent;

        return Result.Success(new ExpenseSummaryDto(
            totalSpent,
            0m,
            allocated,
            totalSpent,
            remaining,
            totalSpent > allocated,
            allocated > 0 && totalSpent >= (allocated * 0.9m)));
    }
}