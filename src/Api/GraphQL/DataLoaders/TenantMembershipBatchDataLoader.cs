using FinFlow.Domain.TenantMemberships;
using GreenDonut;
using HotChocolate;

namespace FinFlow.Api.GraphQL.DataLoaders;

/// <summary>
/// Batches TenantMembership lookups by Id within a single GraphQL request to prevent N+1 queries.
/// </summary>
public sealed class TenantMembershipBatchDataLoader : BatchDataLoader<Guid, TenantMembershipSummary>
{
    private readonly ITenantMembershipRepository _membershipRepository;

    public TenantMembershipBatchDataLoader(
        ITenantMembershipRepository membershipRepository,
        IBatchScheduler batchScheduler,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options)
    {
        _membershipRepository = membershipRepository;
    }

    protected override async Task<IReadOnlyDictionary<Guid, TenantMembershipSummary>> LoadBatchAsync(
        IReadOnlyList<Guid> keys,
        CancellationToken cancellationToken)
    {
        var summaries = await _membershipRepository.GetByIdsAsync(keys, cancellationToken);
        return summaries.ToDictionary(m => m.Id);
    }
}
