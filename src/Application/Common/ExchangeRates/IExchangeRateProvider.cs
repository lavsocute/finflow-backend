using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Common.ExchangeRates;

/// <summary>
/// External source of exchange rates (provider, REST API, ECB feed, etc.).
/// Implementations should be idempotent and side-effect-free at the request level —
/// caching/persistence happens at the <see cref="IExchangeRateService"/> facade layer.
/// </summary>
public interface IExchangeRateProvider
{
    /// <summary>Provider name for logging and audit (e.g. "frankfurter", "exchangerate-host").</summary>
    string Name { get; }

    /// <summary>
    /// Fetch the rate for <c>1 fromCurrency = X toCurrency</c> on the given date.
    /// </summary>
    Task<Result<decimal>> GetRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        CancellationToken cancellationToken = default);
}

public static class ExchangeRateErrors
{
    public static readonly Error ProviderUnavailable =
        new("ExchangeRate.ProviderUnavailable", "Exchange rate provider is unavailable.");

    public static readonly Error RateMissing =
        new("ExchangeRate.RateMissing", "No exchange rate available for the requested currency pair and date.");

    public static readonly Error UnsupportedCurrency =
        new("ExchangeRate.UnsupportedCurrency", "Currency is not supported by the upstream provider.");

    public static readonly Error InvalidProviderResponse =
        new("ExchangeRate.InvalidProviderResponse", "Exchange rate provider returned an unexpected response.");
}
