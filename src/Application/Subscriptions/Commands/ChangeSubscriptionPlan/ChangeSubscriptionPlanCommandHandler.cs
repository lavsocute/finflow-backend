using FinFlow.Application.Common;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.TenantSubscriptions;

namespace FinFlow.Application.Subscriptions.Commands.ChangeSubscriptionPlan;

internal sealed class ChangeSubscriptionPlanCommandHandler
    : ICommandHandler<ChangeSubscriptionPlanCommand, Result>
{
    private readonly ITenantSubscriptionRepository _subscriptionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ChangeSubscriptionPlanCommandHandler(
        ITenantSubscriptionRepository subscriptionRepository,
        IUnitOfWork unitOfWork)
    {
        _subscriptionRepository = subscriptionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        ChangeSubscriptionPlanCommand request,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptionRepository.GetByTenantIdAsync(
            request.TenantId,
            cancellationToken);

        if (subscription is null)
        {
            var now = DateTime.UtcNow;
            var createResult = TenantSubscription.Create(
                request.TenantId,
                request.PlanTier,
                now,
                now.AddMonths(TenantSubscription.MonthlyBillingCycle));
            if (createResult.IsFailure)
                return Result.Failure(createResult.Error);

            _subscriptionRepository.Add(createResult.Value);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var changeResult = subscription.Status == SubscriptionStatus.Active
            ? subscription.ChangePlanTier(request.PlanTier)
            : subscription.StartNewCycle(request.PlanTier, DateTime.UtcNow);
        if (changeResult.IsFailure)
            return changeResult;

        _subscriptionRepository.Update(subscription);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
