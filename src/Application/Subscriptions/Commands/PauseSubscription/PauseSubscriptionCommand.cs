using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Subscriptions.Commands.PauseSubscription;

public sealed record PauseSubscriptionCommand(Guid TenantId) : ICommand<Result>;
