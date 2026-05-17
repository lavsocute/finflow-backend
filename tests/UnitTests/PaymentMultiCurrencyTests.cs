using FinFlow.Domain.Expenses;
using Xunit;

namespace FinFlow.UnitTests;

public class PaymentMultiCurrencyTests
{
    [Fact]
    public void Create_UsdPayment_ConvertsToVndBaseCorrectly()
    {
        var payment = Payment.Create(
            idTenant: Guid.NewGuid(),
            documentId: Guid.NewGuid(),
            idDepartment: Guid.NewGuid(),
            amount: 100m,
            currencyCode: "USD",
            exchangeRate: 25_450m,
            baseCurrencyCode: "VND",
            recordedByMembershipId: Guid.NewGuid(),
            method: PaymentMethod.BankTransfer,
            notes: null);

        Assert.True(payment.IsSuccess);
        Assert.Equal("USD", payment.Value.CurrencyCode);
        Assert.Equal("VND", payment.Value.BaseCurrencyCode);
        Assert.Equal(25_450m, payment.Value.ExchangeRate);
        // 100 USD * 25,450 = 2,545,000 VND
        Assert.Equal(2_545_000m, payment.Value.AmountInBaseCurrency);
    }

    [Fact]
    public void Create_SameCurrencyMustHaveUnitRate()
    {
        var result = Payment.Create(
            idTenant: Guid.NewGuid(),
            documentId: Guid.NewGuid(),
            idDepartment: Guid.NewGuid(),
            amount: 100m,
            currencyCode: "VND",
            exchangeRate: 1.5m,
            baseCurrencyCode: "VND",
            recordedByMembershipId: Guid.NewGuid(),
            method: PaymentMethod.Cash,
            notes: null);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentErrors.SameCurrencyRequiresUnitRate, result.Error);
    }

    [Fact]
    public void Create_InvalidCurrencyCode_Fails()
    {
        var result = Payment.Create(
            idTenant: Guid.NewGuid(),
            documentId: Guid.NewGuid(),
            idDepartment: Guid.NewGuid(),
            amount: 100m,
            currencyCode: "USDS",   // 4 chars
            exchangeRate: 1m,
            baseCurrencyCode: "VND",
            recordedByMembershipId: Guid.NewGuid(),
            method: PaymentMethod.Cash,
            notes: null);

        Assert.True(result.IsFailure);
        Assert.Equal("Currency.InvalidLength", result.Error.Code);
    }

    [Fact]
    public void Create_LowercaseCurrency_NormalizesToUpper()
    {
        var result = Payment.Create(
            idTenant: Guid.NewGuid(),
            documentId: Guid.NewGuid(),
            idDepartment: Guid.NewGuid(),
            amount: 100m,
            currencyCode: "eur",
            exchangeRate: 27_500m,
            baseCurrencyCode: "vnd",
            recordedByMembershipId: Guid.NewGuid(),
            method: PaymentMethod.BankTransfer,
            notes: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("EUR", result.Value.CurrencyCode);
        Assert.Equal("VND", result.Value.BaseCurrencyCode);
    }

    [Fact]
    public void Create_RoundsBaseCurrencyAmountToTwoDecimals()
    {
        // 33.33 USD * 25_450 = 848_249.85 (already 2 decimals, no rounding loss)
        var result = Payment.Create(
            idTenant: Guid.NewGuid(),
            documentId: Guid.NewGuid(),
            idDepartment: Guid.NewGuid(),
            amount: 33.33m,
            currencyCode: "USD",
            exchangeRate: 25_450m,
            baseCurrencyCode: "VND",
            recordedByMembershipId: Guid.NewGuid(),
            method: PaymentMethod.BankTransfer,
            notes: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(848_248.50m, result.Value.AmountInBaseCurrency);
    }
}
