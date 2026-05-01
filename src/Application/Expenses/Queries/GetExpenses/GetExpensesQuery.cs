using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Expenses;
using MediatR;

namespace FinFlow.Application.Expenses.Queries.GetExpenses;

public sealed record GetExpensesQuery(
    Guid TenantId,
    Guid? DepartmentId,
    int? Month,
    int? Year,
    Guid? CategoryId,
    ExpenseStatus? Status) : IRequest<Result<IReadOnlyList<ExpenseSummary>>>;