using FinFlow.Domain.Common;
using FinFlow.Domain.ExchangeRates;
using Xunit;

namespace FinFlow.UnitTests.Domain.ExchangeRates;

public class ExchangeRateHistoryTests
{
    private static readonly DateOnly TestDate = new(2026, 5, 17);

    [Fact]
    public void Create_ValidProviderRate_Succeeds()
    {
        var result = ExchangeRateHistory.Create(
            "USD", "VND", TestDate, 25_450m, ExchangeRateSource.Provider);

        Assert.True(result.IsSuccess);
        Assert.Equal("USD", result.Value.FromCurrency);
        Assert.Equal("VND", result.Value.ToCurrency);
        Assert.Equal(25_450m, result.Value.Rate);
        Assert.Equal(ExchangeRateSource.Provider, result.Value.Source);
        Assert.Null(result.Value.CreatedByMembershipId);
    }

    [Fact]
    public void Create_NormalizesCurrencyCase()
    {
        var result = ExchangeRateHistory.Create(
            "usd", "vnd", TestDate, 25_450m, ExchangeRateSource.Provider);

        Assert.True(result.IsSuccess);
        Assert.Equal("USD", result.Value.FromCurrency);
        Assert.Equal("VND", result.Value.ToCurrency);
    }

    [Fact]
    public void Create_NonPositiveRate_Fails()
    {
        var result = ExchangeRateHistory.Create(
            "USD", "VND", TestDate, 0m, ExchangeRateSource.Provider);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrencyErrors.InvalidRate, result.Error);
    }

    [Fact]
    public void Create_SameCurrencyWithNonUnitRate_Fails()
    {
        var result = ExchangeRateHistory.Create(
            "VND", "VND", TestDate, 1.5m, ExchangeRateSource.Provider);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrencyErrors.MismatchBase, result.Error);
    }

    [Fact]
    public void Create_SameCurrencyWithUnitRate_Succeeds()
    {
        var result = ExchangeRateHistory.Create(
            "VND", "VND", TestDate, 1m, ExchangeRateSource.System);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_InvalidFromCurrency_Fails()
    {
        var result = ExchangeRateHistory.Create(
            "US1", "VND", TestDate, 25_450m, ExchangeRateSource.Provider);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrencyErrors.InvalidFormat, result.Error);
    }

    [Fact]
    public void Create_ManualWithoutActor_Fails()
    {
        var result = ExchangeRateHistory.Create(
            "USD", "VND", TestDate, 25_450m, ExchangeRateSource.Manual);

        Assert.True(result.IsFailure);
        Assert.Equal("ExchangeRate.ManualRequiresActor", result.Error.Code);
    }

    [Fact]
    public void Create_ManualWithActor_Succeeds()
    {
        var actor = Guid.NewGuid();

        var result = ExchangeRateHistory.Create(
            "USD", "VND", TestDate, 25_500m, ExchangeRateSource.Manual, actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(ExchangeRateSource.Manual, result.Value.Source);
        Assert.Equal(actor, result.Value.CreatedByMembershipId);
    }
}
