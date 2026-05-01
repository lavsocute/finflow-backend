using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Budgets.Queries.GetBudgets;

public record GetBudgetsQuery(
    Guid TenantId,
    int? Month,
    int? Year,
    Guid? DepartmentId)
    : IQuery<Result<IReadOnlyList<BudgetSummaryDto>>>;