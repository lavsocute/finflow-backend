using FinFlow.Application.Budgets.DTOs;

namespace FinFlow.Api.GraphQL.Budgets;

public sealed class BudgetSummaryType
{
    public Guid Id { get; set; }
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = null!;
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal AvailableAmount { get; set; }

    public static BudgetSummaryType FromDto(BudgetSummaryDto dto) => new()
    {
        Id = dto.Id,
        DepartmentId = dto.DepartmentId,
        DepartmentName = dto.DepartmentName,
        Month = dto.Month,
        Year = dto.Year,
        AllocatedAmount = dto.AllocatedAmount,
        SpentAmount = dto.SpentAmount,
        AvailableAmount = dto.AvailableAmount
    };
}

public sealed class BudgetDetailType
{
    public Guid Id { get; set; }
    public Guid DepartmentId { get; set; }
    public string DepartmentName { get; set; } = null!;
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal AvailableAmount { get; set; }
    public bool IsOverBudget { get; set; }
    public bool IsNearLimit { get; set; }

    public static BudgetDetailType FromDto(BudgetDetailDto dto) => new()
    {
        Id = dto.Id,
        DepartmentId = dto.DepartmentId,
        DepartmentName = dto.DepartmentName,
        Month = dto.Month,
        Year = dto.Year,
        AllocatedAmount = dto.AllocatedAmount,
        SpentAmount = dto.SpentAmount,
        AvailableAmount = dto.AvailableAmount,
        IsOverBudget = dto.IsOverBudget,
        IsNearLimit = dto.IsNearLimit
    };
}

public sealed class BudgetCheckType
{
    public Guid DepartmentId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal AvailableAmount { get; set; }
    public bool IsOverBudget { get; set; }
    public bool IsNearLimit { get; set; }

    public static BudgetCheckType FromDto(BudgetCheckDto dto) => new()
    {
        DepartmentId = dto.DepartmentId,
        Month = dto.Month,
        Year = dto.Year,
        AllocatedAmount = dto.AllocatedAmount,
        SpentAmount = dto.SpentAmount,
        AvailableAmount = dto.AvailableAmount,
        IsOverBudget = dto.IsOverBudget,
        IsNearLimit = dto.IsNearLimit
    };
}