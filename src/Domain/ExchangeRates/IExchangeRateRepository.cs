namespace FinFlow.Domain.ExchangeRates;

public interface IExchangeRateRepository
{
    /// <summary>Look up exact rate snapshot for given currency pair on given date.</summary>
    Task<ExchangeRateHistory?> GetByKeyAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find the nearest rate within ±<paramref name="windowDays"/> days. Used as a fallback
    /// when no rate exists for the requested date and the external provider is unavailable.
    /// </summary>
    Task<ExchangeRateHistory?> GetNearestAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        int windowDays,
        CancellationToken cancellationToken = default);

    /// <summary>List rates for a date range. Used for reporting and reconciliation views.</summary>
    Task<IReadOnlyList<ExchangeRateHistory>> GetRangeAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    void Add(ExchangeRateHistory entry);
}
