using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Budgets.Commands.UpdateBudget;

public record UpdateBudgetCommand(
    Guid BudgetId,
    Guid TenantId,
    decimal Amount)
    : ICommand<Result<BudgetDetailDto>>;