using FinFlow.Application.Budgets.DTOs;
using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Budgets.Queries.GetBudgetByDepartment;

public record GetBudgetByDepartmentQuery(
    Guid TenantId,
    Guid DepartmentId,
    int Month,
    int Year)
    : IQuery<Result<BudgetDetailDto?>>;