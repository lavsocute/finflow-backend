using FinFlow.Application.Expenses.DTOs;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Expenses.Queries.GetExpenseSummary;

public sealed record GetExpenseSummaryQuery(
    Guid TenantId,
    Guid DepartmentId,
    int Month,
    int Year) : IRequest<Result<ExpenseSummaryDto>>;