using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Budgets.Commands.SetBudgetEnforcementMode;

public sealed record SetBudgetEnforcementModeCommand(
    Guid TenantId,
    Guid BudgetId,
    BudgetEnforcementMode Mode) : ICommand<Result<BudgetDetailDto>>;
