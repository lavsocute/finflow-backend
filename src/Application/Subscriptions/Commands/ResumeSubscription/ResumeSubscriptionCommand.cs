using FinFlow.Application.Common;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Subscriptions.Commands.ResumeSubscription;

public sealed record ResumeSubscriptionCommand(Guid TenantId) : ICommand<Result>;
