using FinFlow.Application.Common.ExchangeRates;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.ExchangeRates;
using FinFlow.Domain.Tenants;

namespace FinFlow.UnitTests.TestStubs;

/// <summary>
/// Tenant repository stub returning a fixed currency. Used by handler tests that
/// need to resolve the tenant base currency without standing up a real repository.
/// </summary>
internal sealed class StubTenantRepository : ITenantRepository
{
    private readonly string _currency;

    public StubTenantRepository(string currency = "VND")
    {
        _currency = currency;
    }

    public Task<TenantSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult<TenantSummary?>(
            new TenantSummary(id, "Test Tenant", "test", TenancyModel.Shared, true, _currency));

    public Task<IReadOnlyList<TenantSummary>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantSummary>>(Array.Empty<TenantSummary>());

    public Task<TenantSummary?> GetByCodeAsync(string tenantCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<TenantSummary?>(null);

    public Task<bool> ExistsByCodeAsync(string tenantCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<TenantSummary>> GetAllActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantSummary>>(Array.Empty<TenantSummary>());

    public void Add(Tenant tenant) { }
    public void Update(Tenant tenant) { }
    public void Remove(Tenant tenant) { }
}

/// <summary>
/// Exchange rate service stub. By default returns 1.0 for any pair.
/// Pass <paramref name="rates"/> to override specific (from→to) lookups.
/// </summary>
internal sealed class StubExchangeRateService : IExchangeRateService
{
    private readonly IReadOnlyDictionary<string, decimal> _rates;
    private readonly bool _shouldFail;

    public StubExchangeRateService(IReadOnlyDictionary<string, decimal>? rates = null, bool shouldFail = false)
    {
        _rates = rates ?? new Dictionary<string, decimal>();
        _shouldFail = shouldFail;
    }

    public Task<Result<ExchangeRateLookupResult>> GetRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        CancellationToken cancellationToken = default)
    {
        if (_shouldFail)
            return Task.FromResult(Result.Failure<ExchangeRateLookupResult>(ExchangeRateErrors.RateMissing));

        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Result.Success(
                new ExchangeRateLookupResult(1m, rateDate, ExchangeRateSource.System)));
        }

        var key = $"{fromCurrency.ToUpperInvariant()}->{toCurrency.ToUpperInvariant()}";
        var rate = _rates.TryGetValue(key, out var r) ? r : 1m;
        return Task.FromResult(Result.Success(
            new ExchangeRateLookupResult(rate, rateDate, ExchangeRateSource.System)));
    }
}
