using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.TenantSubscriptions;

namespace FinFlow.Application.Subscriptions.Commands.ResumeSubscription;

internal sealed class ResumeSubscriptionCommandHandler : ICommandHandler<ResumeSubscriptionCommand, Result>
{
    private readonly ITenantSubscriptionRepository _subscriptionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ResumeSubscriptionCommandHandler(
        ITenantSubscriptionRepository subscriptionRepository,
        IUnitOfWork unitOfWork)
    {
        _subscriptionRepository = subscriptionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ResumeSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);
        if (subscription is null)
            return Result.Failure(TenantSubscriptionErrors.SubscriptionNotFound);

        var resumeResult = subscription.Resume();
        if (resumeResult.IsFailure)
            return resumeResult;

        _subscriptionRepository.Update(subscription);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
