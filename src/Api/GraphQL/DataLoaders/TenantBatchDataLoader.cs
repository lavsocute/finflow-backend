using FinFlow.Domain.Tenants;
using GreenDonut;
using HotChocolate;

namespace FinFlow.Api.GraphQL.DataLoaders;

/// <summary>
/// Batches Tenant lookups by Id within a single GraphQL request to prevent N+1 queries.
/// HotChocolate auto-registers DataLoaders inheriting from BatchDataLoader.
/// </summary>
public sealed class TenantBatchDataLoader : BatchDataLoader<Guid, TenantSummary>
{
    private readonly ITenantRepository _tenantRepository;

    public TenantBatchDataLoader(
        ITenantRepository tenantRepository,
        IBatchScheduler batchScheduler,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options)
    {
        _tenantRepository = tenantRepository;
    }

    protected override async Task<IReadOnlyDictionary<Guid, TenantSummary>> LoadBatchAsync(
        IReadOnlyList<Guid> keys,
        CancellationToken cancellationToken)
    {
        var summaries = await _tenantRepository.GetByIdsAsync(keys, cancellationToken);
        return summaries.ToDictionary(t => t.Id);
    }
}
