namespace FinFlow.Application.Common.Abstractions;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;
}

public static class CacheKeys
{
    public static string Department(Guid id, Guid tenantId) => $"dept:{tenantId}:{id}";
    public static string DepartmentList(Guid tenantId) => $"dept:list:{tenantId}";
    public static string Vendor(Guid id, Guid tenantId) => $"vendor:{tenantId}:{id}";
    public static string VendorList(Guid tenantId) => $"vendor:list:{tenantId}";
}