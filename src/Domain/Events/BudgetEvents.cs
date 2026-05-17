using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Events;

public sealed record BudgetCreatedDomainEvent(
    Guid BudgetId,
    Guid TenantId,
    Guid DepartmentId,
    int Month,
    int Year,
    decimal AllocatedAmount) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record BudgetUpdatedDomainEvent(
    Guid BudgetId,
    Guid TenantId,
    Guid DepartmentId,
    decimal AllocatedAmount,
    decimal SpentAmount) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
