using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Common.ExchangeRates;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Common;
using FinFlow.Domain.ExchangeRates;
using FinFlow.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinFlow.Infrastructure.ExchangeRates;

/// <summary>
/// Default <see cref="IExchangeRateService"/> implementation. Resolves rates by chaining:
/// 1. Distributed cache (1h TTL) for the hot path.
/// 2. Persistent <see cref="ExchangeRateHistory"/> table.
/// 3. External <see cref="IExchangeRateProvider"/> chain (with persistence on success).
/// 4. Nearest-date fallback within ±7 days when provider is unavailable.
/// </summary>
internal sealed class ExchangeRateService : IExchangeRateService
{
    private const int FallbackWindowDays = 7;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly IEnumerable<IExchangeRateProvider> _providers;
    private readonly IExchangeRateRepository _rateRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<ExchangeRateService> _logger;

    public ExchangeRateService(
        IEnumerable<IExchangeRateProvider> providers,
        IExchangeRateRepository rateRepository,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<ExchangeRateService> logger)
    {
        _providers = providers;
        _rateRepository = rateRepository;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<ExchangeRateLookupResult>> GetRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        CancellationToken cancellationToken = default)
    {
        var fromResult = Currency.Create(fromCurrency);
        if (fromResult.IsFailure) return Result.Failure<ExchangeRateLookupResult>(fromResult.Error);

        var toResult = Currency.Create(toCurrency);
        if (toResult.IsFailure) return Result.Failure<ExchangeRateLookupResult>(toResult.Error);

        var from = fromResult.Value.Code;
        var to = toResult.Value.Code;

        // Same currency — short-circuit.
        if (from == to)
            return Result.Success(new ExchangeRateLookupResult(1m, rateDate, ExchangeRateSource.System));

        var cacheKey = BuildCacheKey(from, to, rateDate);

        // 1) Cache.
        try
        {
            var cached = await _cache.GetAsync<CachedRate>(cacheKey, cancellationToken);
            if (cached is not null && cached.Rate > 0)
                return Result.Success(new ExchangeRateLookupResult(cached.Rate, cached.EffectiveDate, cached.Source));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Exchange rate cache get failed for {Key}", cacheKey);
        }

        // 2) Persisted history (exact match for this date).
        var dbHit = await _rateRepository.GetByKeyAsync(from, to, rateDate, cancellationToken);
        if (dbHit is not null)
        {
            await TryPutCacheAsync(cacheKey, new CachedRate(dbHit.Rate, dbHit.RateDate, dbHit.Source), cancellationToken);
            return Result.Success(new ExchangeRateLookupResult(dbHit.Rate, dbHit.RateDate, dbHit.Source));
        }

        // 3) External provider chain.
        foreach (var provider in _providers)
        {
            var providerResult = await provider.GetRateAsync(from, to, rateDate, cancellationToken);
            if (providerResult.IsFailure)
            {
                _logger.LogDebug(
                    "Provider {Provider} failed for {From}->{To}: {Error}",
                    provider.Name, from, to, providerResult.Error.Description);
                continue;
            }

            var historyResult = ExchangeRateHistory.Create(from, to, rateDate, providerResult.Value, ExchangeRateSource.Provider);
            if (historyResult.IsSuccess)
            {
                _rateRepository.Add(historyResult.Value);
                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    // Race: another caller persisted same key. Safe to ignore — unique constraint blocks duplicates.
                    _logger.LogDebug(ex, "Persisting provider rate {From}->{To} failed (likely race)", from, to);
                }
            }

            var lookup = new ExchangeRateLookupResult(providerResult.Value, rateDate, ExchangeRateSource.Provider);
            await TryPutCacheAsync(cacheKey, new CachedRate(lookup.Rate, lookup.EffectiveDate, lookup.Source), cancellationToken);
            return Result.Success(lookup);
        }

        // 4) Nearest fallback (within window).
        var nearest = await _rateRepository.GetNearestAsync(from, to, rateDate, FallbackWindowDays, cancellationToken);
        if (nearest is not null)
        {
            _logger.LogInformation(
                "Using nearest exchange rate for {From}->{To} requested {Date} resolved {Effective}",
                from, to, rateDate, nearest.RateDate);
            var lookup = new ExchangeRateLookupResult(nearest.Rate, nearest.RateDate, nearest.Source);
            await TryPutCacheAsync(cacheKey, new CachedRate(lookup.Rate, lookup.EffectiveDate, lookup.Source), cancellationToken);
            return Result.Success(lookup);
        }

        return Result.Failure<ExchangeRateLookupResult>(ExchangeRateErrors.RateMissing);
    }

    private static string BuildCacheKey(string from, string to, DateOnly rateDate) =>
        $"fxrate:{from}:{to}:{rateDate:yyyyMMdd}";

    private async Task TryPutCacheAsync(string key, CachedRate value, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(key, value, CacheTtl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Exchange rate cache set failed for {Key}", key);
        }
    }

    private sealed record CachedRate(decimal Rate, DateOnly EffectiveDate, ExchangeRateSource Source);
}
