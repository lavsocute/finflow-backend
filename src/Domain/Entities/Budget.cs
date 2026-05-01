using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Entities;

public sealed class Budget : Entity, IMultiTenant
{
    private Budget(
        Guid id,
        Guid idTenant,
        Guid idDepartment,
        int month,
        int year,
        decimal allocatedAmount,
        decimal spentAmount,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        IdTenant = idTenant;
        IdDepartment = idDepartment;
        Month = month;
        Year = year;
        AllocatedAmount = allocatedAmount;
        SpentAmount = spentAmount;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    private Budget() { }

    public Guid IdTenant { get; private set; }
    public Guid IdDepartment { get; private set; }
    public int Month { get; private set; }
    public int Year { get; private set; }
    public decimal AllocatedAmount { get; private set; }
    public decimal SpentAmount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public decimal AvailableAmount => AllocatedAmount - SpentAmount;
    public bool IsOverBudget => SpentAmount > AllocatedAmount;
    public bool IsNearLimit => AllocatedAmount > 0 && SpentAmount >= (AllocatedAmount * 0.9m);

    public static Result<Budget> Create(
        Guid idTenant,
        Guid idDepartment,
        int month,
        int year,
        decimal allocatedAmount)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<Budget>(BudgetErrors.TenantRequired);
        if (idDepartment == Guid.Empty)
            return Result.Failure<Budget>(BudgetErrors.DepartmentRequired);
        if (month < 1 || month > 12)
            return Result.Failure<Budget>(BudgetErrors.InvalidMonth);
        if (year < 2000 || year > 2100)
            return Result.Failure<Budget>(BudgetErrors.InvalidYear);
        if (allocatedAmount < 0)
            return Result.Failure<Budget>(BudgetErrors.InvalidAmount);

        var now = DateTime.UtcNow;
        return Result.Success(new Budget(
            Guid.NewGuid(),
            idTenant,
            idDepartment,
            month,
            year,
            allocatedAmount,
            spentAmount: 0,
            createdAt: now,
            updatedAt: now));
    }

    public Result UpdateAmount(decimal amount)
    {
        if (amount < 0)
            return Result.Failure(BudgetErrors.InvalidAmount);

        AllocatedAmount = amount;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public void RecalculateSpent(decimal spentAmount)
    {
        if (spentAmount < 0)
            spentAmount = 0;
        SpentAmount = spentAmount;
        UpdatedAt = DateTime.UtcNow;
    }
}