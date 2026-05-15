using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantSubscriptions;

namespace FinFlow.Application.Subscriptions.Commands.PauseSubscription;

internal sealed class PauseSubscriptionCommandHandler : ICommandHandler<PauseSubscriptionCommand, Result>
{
    private readonly ITenantSubscriptionRepository _subscriptionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PauseSubscriptionCommandHandler(
        ITenantSubscriptionRepository subscriptionRepository,
        IUnitOfWork unitOfWork)
    {
        _subscriptionRepository = subscriptionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(PauseSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);
        if (subscription is null)
            return Result.Failure(TenantSubscriptionErrors.SubscriptionNotFound);

        var pauseResult = subscription.Pause();
        if (pauseResult.IsFailure)
            return pauseResult;

        _subscriptionRepository.Update(subscription);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
