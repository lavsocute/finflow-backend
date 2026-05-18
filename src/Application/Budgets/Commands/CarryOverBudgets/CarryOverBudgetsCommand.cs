using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Budgets.Commands.CarryOverBudgets;

/// <summary>
/// At the start of a new month an admin (or scheduled job) calls this to copy
/// budget allocations forward and roll any unused capacity from the previous
/// month into <c>CarryOverFromPreviousMonth</c>. Carry-over policy is
/// <paramref name="CarryOverPercentage"/>: 0 = no carry, 100 = full unused
/// pool, 50 = half. Stays per-call rather than per-tenant config so admins
/// can tune end-of-quarter exceptions.
/// </summary>
public sealed record CarryOverBudgetsCommand(
    Guid TenantId,
    int FromMonth,
    int FromYear,
    int ToMonth,
    int ToYear,
    decimal CarryOverPercentage = 0m) : ICommand<Result<int>>;
