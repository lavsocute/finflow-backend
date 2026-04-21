using FinFlow.Domain.Entities;

namespace FinFlow.Domain.TenantSubscriptions;

public interface ITenantSubscriptionRepository
{
    Task<TenantSubscription?> GetByTenantIdAsync(Guid idTenant, CancellationToken cancellationToken = default);
    void Add(TenantSubscription subscription);
    void Update(TenantSubscription subscription);
}
