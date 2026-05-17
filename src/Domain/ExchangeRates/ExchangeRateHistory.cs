using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Common;

namespace FinFlow.Domain.ExchangeRates;

public enum ExchangeRateSource
{
    /// <summary>Fetched automatically from external provider (e.g. Frankfurter).</summary>
    Provider,
    /// <summary>Inserted manually by tenant administrator.</summary>
    Manual,
    /// <summary>System-seeded (initial data, fallback).</summary>
    System
}

/// <summary>
/// Snapshot of an exchange rate for a specific (from, to, date) tuple.
/// Used as the cache layer for <see cref="IExchangeRateProvider"/> and as
/// audit trail for manual rate overrides. NOT multi-tenant — rates are global.
/// </summary>
public sealed class ExchangeRateHistory : Entity
{
    private ExchangeRateHistory(
        Guid id,
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        decimal rate,
        ExchangeRateSource source,
        DateTime createdAt,
        Guid? createdByMembershipId)
    {
        Id = id;
        FromCurrency = fromCurrency;
        ToCurrency = toCurrency;
        RateDate = rateDate;
        Rate = rate;
        Source = source;
        CreatedAt = createdAt;
        CreatedByMembershipId = createdByMembershipId;
    }

    private ExchangeRateHistory() { }

    public string FromCurrency { get; private set; } = null!;
    public string ToCurrency { get; private set; } = null!;
    public DateOnly RateDate { get; private set; }
    public decimal Rate { get; private set; }
    public ExchangeRateSource Source { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedByMembershipId { get; private set; }

    public static Result<ExchangeRateHistory> Create(
        string fromCurrency,
        string toCurrency,
        DateOnly rateDate,
        decimal rate,
        ExchangeRateSource source,
        Guid? createdByMembershipId = null)
    {
        var fromResult = Currency.Create(fromCurrency);
        if (fromResult.IsFailure) return Result.Failure<ExchangeRateHistory>(fromResult.Error);

        var toResult = Currency.Create(toCurrency);
        if (toResult.IsFailure) return Result.Failure<ExchangeRateHistory>(toResult.Error);

        if (rate <= 0)
            return Result.Failure<ExchangeRateHistory>(CurrencyErrors.InvalidRate);

        if (fromResult.Value.Code == toResult.Value.Code && rate != 1m)
            return Result.Failure<ExchangeRateHistory>(CurrencyErrors.MismatchBase);

        if (source == ExchangeRateSource.Manual && createdByMembershipId is null)
            return Result.Failure<ExchangeRateHistory>(
                new Error("ExchangeRate.ManualRequiresActor", "Manual exchange rate must include actor membership."));

        return Result.Success(new ExchangeRateHistory(
            Guid.NewGuid(),
            fromResult.Value.Code,
            toResult.Value.Code,
            rateDate,
            rate,
            source,
            DateTime.UtcNow,
            createdByMembershipId));
    }
}
