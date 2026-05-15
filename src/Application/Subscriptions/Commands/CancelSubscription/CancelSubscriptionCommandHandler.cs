using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantSubscriptions;

namespace FinFlow.Application.Subscriptions.Commands.CancelSubscription;

internal sealed class CancelSubscriptionCommandHandler : ICommandHandler<CancelSubscriptionCommand, Result>
{
    private readonly ITenantSubscriptionRepository _subscriptionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CancelSubscriptionCommandHandler(
        ITenantSubscriptionRepository subscriptionRepository,
        IUnitOfWork unitOfWork)
    {
        _subscriptionRepository = subscriptionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CancelSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);
        if (subscription is null)
            return Result.Failure(TenantSubscriptionErrors.SubscriptionNotFound);

        var cancelResult = subscription.Cancel();
        if (cancelResult.IsFailure)
            return cancelResult;

        _subscriptionRepository.Update(subscription);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
