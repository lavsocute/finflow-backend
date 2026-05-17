using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Common.ExchangeRates;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.ExchangeRates;
using FinFlow.Infrastructure.ExchangeRates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinFlow.UnitTests.Infrastructure.ExchangeRates;

public class ExchangeRateServiceTests
{
    private static readonly DateOnly TestDate = new(2026, 5, 17);

    [Fact]
    public async Task GetRate_SameCurrency_ShortCircuitsWithoutCallingProvider()
    {
        var provider = new RecordingProvider(rate: 99m);
        var repo = new InMemoryRepo();
        var service = BuildService(provider, repo);

        var result = await service.GetRateAsync("VND", "VND", TestDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(1m, result.Value.Rate);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task GetRate_DbHit_AvoidsProvider()
    {
        var provider = new RecordingProvider(rate: 99m);
        var repo = new InMemoryRepo();
        repo.Add(ExchangeRateHistory.Create("USD", "VND", TestDate, 25_450m, ExchangeRateSource.Manual, Guid.NewGuid()).Value);
        var service = BuildService(provider, repo);

        var result = await service.GetRateAsync("USD", "VND", TestDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(25_450m, result.Value.Rate);
        Assert.Equal(ExchangeRateSource.Manual, result.Value.Source);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task GetRate_NoDbEntry_FallsBackToProvider_AndPersists()
    {
        var provider = new RecordingProvider(rate: 25_450m);
        var repo = new InMemoryRepo();
        var service = BuildService(provider, repo);

        var result = await service.GetRateAsync("USD", "VND", TestDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(25_450m, result.Value.Rate);
        Assert.Equal(ExchangeRateSource.Provider, result.Value.Source);
        Assert.Equal(1, provider.CallCount);
        Assert.Single(repo.Entries);
    }

    [Fact]
    public async Task GetRate_ProviderFails_FallsBackToNearestDate()
    {
        var provider = new RecordingProvider(rate: 0m, fail: true);
        var repo = new InMemoryRepo();
        // Insert a rate from 3 days earlier — within fallback window
        repo.Add(ExchangeRateHistory.Create("USD", "VND", TestDate.AddDays(-3), 25_400m, ExchangeRateSource.Provider).Value);
        var service = BuildService(provider, repo);

        var result = await service.GetRateAsync("USD", "VND", TestDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(25_400m, result.Value.Rate);
        Assert.Equal(TestDate.AddDays(-3), result.Value.EffectiveDate);
    }

    [Fact]
    public async Task GetRate_NoData_AndProviderFails_ReturnsRateMissing()
    {
        var provider = new RecordingProvider(rate: 0m, fail: true);
        var repo = new InMemoryRepo();
        var service = BuildService(provider, repo);

        var result = await service.GetRateAsync("USD", "VND", TestDate);

        Assert.True(result.IsFailure);
        Assert.Equal(ExchangeRateErrors.RateMissing, result.Error);
    }

    [Fact]
    public async Task GetRate_PrefersManualOverProvider_OnSameDate()
    {
        var provider = new RecordingProvider(rate: 99m);
        var repo = new InMemoryRepo();
        repo.Add(ExchangeRateHistory.Create("USD", "VND", TestDate, 25_400m, ExchangeRateSource.Provider).Value);
        repo.Add(ExchangeRateHistory.Create("USD", "VND", TestDate, 25_500m, ExchangeRateSource.Manual, Guid.NewGuid()).Value);
        var service = BuildService(provider, repo);

        var result = await service.GetRateAsync("USD", "VND", TestDate);

        Assert.True(result.IsSuccess);
        Assert.Equal(25_500m, result.Value.Rate);
        Assert.Equal(ExchangeRateSource.Manual, result.Value.Source);
    }

    private static ExchangeRateService BuildService(IExchangeRateProvider provider, IExchangeRateRepository repo) =>
        new(
            new[] { provider },
            repo,
            new NoOpUnitOfWork(),
            new NoOpCacheService(),
            NullLogger<ExchangeRateService>.Instance);

    private sealed class RecordingProvider : IExchangeRateProvider
    {
        private readonly decimal _rate;
        private readonly bool _fail;

        public RecordingProvider(decimal rate, bool fail = false)
        {
            _rate = rate;
            _fail = fail;
        }

        public string Name => "test-provider";
        public int CallCount { get; private set; }

        public Task<Result<decimal>> GetRateAsync(string fromCurrency, string toCurrency, DateOnly rateDate, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_fail
                ? Result.Failure<decimal>(ExchangeRateErrors.ProviderUnavailable)
                : Result.Success(_rate));
        }
    }

    private sealed class InMemoryRepo : IExchangeRateRepository
    {
        public List<ExchangeRateHistory> Entries { get; } = new();

        public Task<ExchangeRateHistory?> GetByKeyAsync(string fromCurrency, string toCurrency, DateOnly rateDate, CancellationToken cancellationToken = default)
        {
            var hit = Entries
                .Where(e => e.FromCurrency == fromCurrency && e.ToCurrency == toCurrency && e.RateDate == rateDate)
                // mirrors production preference: manual > provider > system
                .OrderBy(e => e.Source switch
                {
                    ExchangeRateSource.Manual => 0,
                    ExchangeRateSource.Provider => 1,
                    _ => 2
                })
                .FirstOrDefault();
            return Task.FromResult<ExchangeRateHistory?>(hit);
        }

        public Task<ExchangeRateHistory?> GetNearestAsync(string fromCurrency, string toCurrency, DateOnly rateDate, int windowDays, CancellationToken cancellationToken = default)
        {
            var min = rateDate.AddDays(-windowDays);
            var max = rateDate.AddDays(windowDays);
            var hit = Entries
                .Where(e => e.FromCurrency == fromCurrency && e.ToCurrency == toCurrency
                    && e.RateDate >= min && e.RateDate <= max)
                .OrderBy(e => Math.Abs(e.RateDate.DayNumber - rateDate.DayNumber))
                .FirstOrDefault();
            return Task.FromResult<ExchangeRateHistory?>(hit);
        }

        public Task<IReadOnlyList<ExchangeRateHistory>> GetRangeAsync(string fromCurrency, string toCurrency, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExchangeRateHistory>>(Entries
                .Where(e => e.FromCurrency == fromCurrency && e.ToCurrency == toCurrency && e.RateDate >= fromDate && e.RateDate <= toDate)
                .ToList());

        public void Add(ExchangeRateHistory entry) => Entries.Add(entry);
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class NoOpCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class => Task.FromResult<T?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> IncrementWithExpiryAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class => factory();
    }
}
