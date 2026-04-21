using FinFlow.Application.Subscriptions.DTOs.Responses;
using FinFlow.Domain.Abstractions;
using MediatR;

namespace FinFlow.Application.Subscriptions.Queries.GetCurrentSubscription;

public sealed record GetCurrentSubscriptionQuery(Guid TenantId) : IRequest<Result<CurrentSubscriptionResponse>>;
