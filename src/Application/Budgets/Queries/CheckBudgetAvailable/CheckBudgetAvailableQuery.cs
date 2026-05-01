using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Budgets.Queries.CheckBudgetAvailable;

public record CheckBudgetAvailableQuery(
    Guid TenantId,
    Guid DepartmentId,
    int Month,
    int Year)
    : IQuery<Result<BudgetCheckDto>>;