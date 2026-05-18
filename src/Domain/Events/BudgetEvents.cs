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

public sealed record BudgetExceededDomainEvent(
    Guid BudgetId,
    Guid TenantId,
    Guid DepartmentId,
    int Month,
    int Year,
    decimal AllocatedAmount,
    decimal CommittedAmount,
    decimal SpentAmount,
    decimal OverAmount,
    FinFlow.Domain.Enums.BudgetExceededTrigger Trigger) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record BudgetWarningThresholdReachedDomainEvent(
    Guid BudgetId,
    Guid TenantId,
    Guid DepartmentId,
    decimal UtilizationPercent,
    decimal Threshold) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record BudgetOverrideUsedDomainEvent(
    Guid BudgetId,
    Guid TenantId,
    Guid OverrodeByMembershipId,
    string Justification,
    decimal OverAmount,
    Guid SourceEntityId,
    string SourceEntityType) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
