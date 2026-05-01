using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Expenses.Commands.RejectExpense;

public sealed record RejectExpenseCommand(Guid ExpenseId, string Reason) : IRequest<Result<Unit>>;