using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Expenses;
using FinFlow.Domain.Interfaces;
using MediatR;

namespace FinFlow.Application.Expenses.Queries.GetExpenses;

internal sealed class GetExpensesQueryHandler : IRequestHandler<GetExpensesQuery, Result<IReadOnlyList<ExpenseSummary>>>
{
    private readonly IExpenseRepository _expenseRepository;

    public GetExpensesQueryHandler(IExpenseRepository expenseRepository) => _expenseRepository = expenseRepository;

    public async Task<Result<IReadOnlyList<ExpenseSummary>>> Handle(GetExpensesQuery request, CancellationToken cancellationToken)
    {
        if (request.DepartmentId.HasValue && request.Month.HasValue && request.Year.HasValue)
        {
            var expenses = await _expenseRepository.GetByDepartmentAndPeriodAsync(
                request.DepartmentId.Value,
                request.Month.Value,
                request.Year.Value,
                request.Status,
                cancellationToken);

            return Result.Success((IReadOnlyList<ExpenseSummary>)expenses);
        }

        return Result.Success<IReadOnlyList<ExpenseSummary>>(Array.Empty<ExpenseSummary>());
    }
}