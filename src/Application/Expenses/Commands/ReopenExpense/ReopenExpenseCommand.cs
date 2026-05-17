using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Expenses.Commands.ReopenExpense;

public sealed record ReopenExpenseCommand(Guid ExpenseId, string Reason) : IRequest<Result<Unit>>;
