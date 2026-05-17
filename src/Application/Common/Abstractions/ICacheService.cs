namespace FinFlow.Application.Common.Abstractions;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all cache entries whose key starts with the given prefix.
    /// Used for bulk invalidation (e.g. response cache when documents are reindexed).
    /// </summary>
    Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomic increment counter with TTL set only on first creation.
    /// Used for sliding-window rate limiting.
    /// Returns the new counter value.
    /// </summary>
    Task<long> IncrementWithExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default);

    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;
}

public static class CacheKeys
{
    public static string Department(Guid id, Guid tenantId) => $"dept:{tenantId}:{id}";
    public static string DepartmentList(Guid tenantId) => $"dept:list:{tenantId}";
    public static string Vendor(Guid id, Guid tenantId) => $"vendor:{tenantId}:{id}";
    public static string VendorList(Guid tenantId) => $"vendor:list:{tenantId}";
}