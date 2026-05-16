using FinFlow.Domain.Departments;
using GreenDonut;
using HotChocolate;

namespace FinFlow.Api.GraphQL.DataLoaders;

/// <summary>
/// Batches Department lookups by Id within a single GraphQL request to prevent N+1 queries.
/// </summary>
public sealed class DepartmentBatchDataLoader : BatchDataLoader<Guid, DepartmentSummary>
{
    private readonly IDepartmentRepository _departmentRepository;

    public DepartmentBatchDataLoader(
        IDepartmentRepository departmentRepository,
        IBatchScheduler batchScheduler,
        DataLoaderOptions? options = null)
        : base(batchScheduler, options)
    {
        _departmentRepository = departmentRepository;
    }

    protected override async Task<IReadOnlyDictionary<Guid, DepartmentSummary>> LoadBatchAsync(
        IReadOnlyList<Guid> keys,
        CancellationToken cancellationToken)
    {
        var summaries = await _departmentRepository.GetByIdsAsync(keys, cancellationToken);
        return summaries.ToDictionary(d => d.Id);
    }
}
