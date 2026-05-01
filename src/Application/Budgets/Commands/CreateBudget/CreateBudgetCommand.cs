using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Budgets.Commands.CreateBudget;

public record CreateBudgetCommand(
    Guid TenantId,
    Guid DepartmentId,
    int Month,
    int Year,
    decimal Amount)
    : ICommand<Result<BudgetDetailDto>>;