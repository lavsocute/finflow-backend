using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantSubscriptions;

namespace FinFlow.Application.Subscriptions.Commands.ReactivateSubscription;

/// <summary>
/// Reactivates a PastDue or Expired subscription (e.g., after payment succeeds).
/// Renews the billing period starting from now.
/// </summary>
internal sealed class ReactivateSubscriptionCommandHandler : ICommandHandler<ReactivateSubscriptionCommand, Result>
{
    private readonly ITenantSubscriptionRepository _subscriptionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ReactivateSubscriptionCommandHandler(
        ITenantSubscriptionRepository subscriptionRepository,
        IUnitOfWork unitOfWork)
    {
        _subscriptionRepository = subscriptionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReactivateSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);
        if (subscription is null)
            return Result.Failure(TenantSubscriptionErrors.SubscriptionNotFound);

        var reactivateResult = subscription.Reactivate(DateTime.UtcNow);
        if (reactivateResult.IsFailure)
            return reactivateResult;

        _subscriptionRepository.Update(subscription);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
