using FinFlow.Infrastructure.Ocr.LlmVision;
using Xunit;

namespace FinFlow.UnitTests.Infrastructure.Ocr;

public class LlmVisionOcrParserCurrencyTests
{
    private const string SampleBase = """
        {
            "vendorName": "Acme Cloud",
            "reference": "INV-001",
            "documentDate": "2026-05-17",
            "extractedInvoiceDueDate": null,
            "category": "Software",
            "vendorTaxId": null,
            "subtotal": 100,
            "vat": 0,
            "totalAmount": 100,
            "lineItems": [
                {"itemName": "Cloud Compute", "quantity": 1, "unitPrice": 100, "total": 100}
            ]
        """;

    [Fact]
    public void Parse_WithIso4217Currency_ExtractsCode()
    {
        var json = SampleBase + ", \"currencyCode\": \"USD\"}";

        var result = LlmVisionOcrParser.Parse(json, "test");

        Assert.True(result.IsSuccess);
        Assert.Equal("USD", result.Value.CurrencyCode);
    }

    [Fact]
    public void Parse_WithLowercaseCurrency_NormalizesToUpper()
    {
        var json = SampleBase + ", \"currencyCode\": \"eur\"}";

        var result = LlmVisionOcrParser.Parse(json, "test");

        Assert.True(result.IsSuccess);
        Assert.Equal("EUR", result.Value.CurrencyCode);
    }

    [Fact]
    public void Parse_WithNullCurrency_ReturnsNull()
    {
        var json = SampleBase + ", \"currencyCode\": null}";

        var result = LlmVisionOcrParser.Parse(json, "test");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CurrencyCode);
    }

    [Fact]
    public void Parse_WithoutCurrencyField_ReturnsNull()
    {
        var json = SampleBase + "}";

        var result = LlmVisionOcrParser.Parse(json, "test");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CurrencyCode);
    }

    [Fact]
    public void Parse_WithInvalidCurrencyLength_ReturnsNull()
    {
        var json = SampleBase + ", \"currencyCode\": \"USDS\"}";

        var result = LlmVisionOcrParser.Parse(json, "test");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CurrencyCode);
    }

    [Fact]
    public void Parse_WithCurrencySymbolInsteadOfCode_ReturnsNull()
    {
        var json = SampleBase + ", \"currencyCode\": \"$\"}";

        var result = LlmVisionOcrParser.Parse(json, "test");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CurrencyCode);
    }

    [Fact]
    public void Parse_WithDigitsInCurrency_ReturnsNull()
    {
        var json = SampleBase + ", \"currencyCode\": \"US1\"}";

        var result = LlmVisionOcrParser.Parse(json, "test");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CurrencyCode);
    }
}
