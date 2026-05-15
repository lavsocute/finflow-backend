using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Subscriptions.Commands.ReactivateSubscription;

public sealed record ReactivateSubscriptionCommand(Guid TenantId) : ICommand<Result>;
