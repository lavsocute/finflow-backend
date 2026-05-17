using FinFlow.Domain.ExchangeRates;

namespace FinFlow.Api.GraphQL.ExchangeRates;

public sealed class ExchangeRateResponse
{
    public string FromCurrency { get; set; } = null!;
    public string ToCurrency { get; set; } = null!;
    public DateOnly RateDate { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public decimal Rate { get; set; }
    public string Source { get; set; } = null!;

    public static ExchangeRateResponse FromLookup(
        string from,
        string to,
        DateOnly requested,
        FinFlow.Application.Common.ExchangeRates.ExchangeRateLookupResult lookup) => new()
    {
        FromCurrency = from,
        ToCurrency = to,
        RateDate = requested,
        EffectiveDate = lookup.EffectiveDate,
        Rate = lookup.Rate,
        Source = lookup.Source.ToString()
    };
}

public sealed record SetManualExchangeRateInput(
    string FromCurrency,
    string ToCurrency,
    DateOnly RateDate,
    decimal Rate);
