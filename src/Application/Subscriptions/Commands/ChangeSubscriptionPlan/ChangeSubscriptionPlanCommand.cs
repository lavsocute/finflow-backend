using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Enums;

namespace FinFlow.Application.Subscriptions.Commands.ChangeSubscriptionPlan;

public sealed record ChangeSubscriptionPlanCommand(Guid TenantId, PlanTier PlanTier)
    : ICommand<Result>;
