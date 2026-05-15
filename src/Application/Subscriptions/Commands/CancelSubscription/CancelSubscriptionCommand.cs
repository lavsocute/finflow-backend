using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Subscriptions.Commands.CancelSubscription;

public sealed record CancelSubscriptionCommand(Guid TenantId) : ICommand<Result>;
