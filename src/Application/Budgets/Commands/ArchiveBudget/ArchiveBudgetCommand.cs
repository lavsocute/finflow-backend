using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Budgets.Commands.ArchiveBudget;

public sealed record ArchiveBudgetCommand(Guid TenantId, Guid BudgetId) : ICommand<Result>;
