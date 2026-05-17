using FinFlow.Domain.Common;
using Xunit;

namespace FinFlow.UnitTests.Domain.Common;

public class CurrencyTests
{
    [Theory]
    [InlineData("VND")]
    [InlineData("usd")]      // lowercase normalized
    [InlineData("  EUR  ")]  // whitespace trimmed
    [InlineData("Jpy")]
    public void Create_ValidCode_ReturnsUpperInvariant(string input)
    {
        var result = Currency.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(input.Trim().ToUpperInvariant(), result.Value.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_EmptyOrNull_ReturnsRequiredError(string? input)
    {
        var result = Currency.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrencyErrors.Required, result.Error);
    }

    [Theory]
    [InlineData("US")]      // 2 chars
    [InlineData("USDS")]    // 4 chars
    public void Create_WrongLength_ReturnsInvalidLength(string input)
    {
        var result = Currency.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrencyErrors.InvalidLength, result.Error);
    }

    [Theory]
    [InlineData("US1")]
    [InlineData("12$")]
    [InlineData("EU.")]
    public void Create_NonAlpha_ReturnsInvalidFormat(string input)
    {
        var result = Currency.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal(CurrencyErrors.InvalidFormat, result.Error);
    }

    [Fact]
    public void StaticConstants_AreValid()
    {
        Assert.Equal("VND", Currency.Vnd.Code);
        Assert.Equal("USD", Currency.Usd.Code);
        Assert.Equal("EUR", Currency.Eur.Code);
        Assert.Equal("GBP", Currency.Gbp.Code);
        Assert.Equal("JPY", Currency.Jpy.Code);
        Assert.Equal("SGD", Currency.Sgd.Code);
        Assert.Equal("CNY", Currency.Cny.Code);
    }

    [Fact]
    public void ImplicitStringConversion_ReturnsCode()
    {
        string code = Currency.Usd;
        Assert.Equal("USD", code);
    }

    [Fact]
    public void ToString_ReturnsCode()
    {
        Assert.Equal("VND", Currency.Vnd.ToString());
    }
}
