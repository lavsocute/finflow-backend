namespace FinFlow.Application.Budgets.DTOs;

public record BudgetSummaryDto(
    Guid Id,
    Guid DepartmentId,
    string DepartmentName,
    int Month,
    int Year,
    decimal AllocatedAmount,
    decimal SpentAmount,
    decimal AvailableAmount);

public record BudgetDetailDto(
    Guid Id,
    Guid DepartmentId,
    string DepartmentName,
    int Month,
    int Year,
    decimal AllocatedAmount,
    decimal SpentAmount,
    decimal AvailableAmount,
    bool IsOverBudget,
    bool IsNearLimit);

public record BudgetCheckDto(
    Guid DepartmentId,
    int Month,
    int Year,
    decimal AllocatedAmount,
    decimal SpentAmount,
    decimal AvailableAmount,
    bool IsOverBudget,
    bool IsNearLimit);