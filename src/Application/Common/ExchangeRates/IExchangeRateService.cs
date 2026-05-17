using FinFlow.Domain.Abstractions;
using FinFlow.Domain.ExchangeRates;

namespace FinFlow.Application.Common.ExchangeRates;

public sealed record ExchangeRateLookupResult(
    decimal Rate,
    DateOnly EffectiveDate,
    ExchangeRateSource Source);

/// <summary>
/// Application-level facade for exchange rate lookups. Combines:
///  1. In-memory cache (fast hot path)
///  2. Persistent <see cref="ExchangeRateHistory"/> table
///  3. External <see cref="IExchangeRateProvider"/> chain
///  4. Nearest-date fallback (within configured window)
/// </summary>
public interface IExchangeRateService
{
    /// <summary>
    /// Resolve the rate for <c>1 fromCurrency = X toCurrency</c>. Same currency returns 1.0
    /// without touching cache/DB/provider.
    /// </summary>
    Task<Result<ExchangeRateLookupResult>> GetRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        CancellationToken cancellationToken = default);
}
