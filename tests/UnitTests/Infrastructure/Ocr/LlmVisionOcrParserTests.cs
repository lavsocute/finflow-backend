using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Ocr.LlmVision;

namespace FinFlow.UnitTests.Infrastructure.Ocr;

public sealed class LlmVisionOcrParserTests
{
    [Fact]
    public void Parse_ReturnsResult_ForPlainJson()
    {
        const string json =
            """
            {"vendorName":"Acme Cloud Ltd.","reference":"INV-2026-0042","documentDate":"2026-04-18","dueDate":"2026-05-02","category":"Software & SaaS","vendorTaxId":"TX-123","subtotal":1200.00,"vat":240.00,"totalAmount":1440.00,"lineItems":[{"itemName":"Cloud Compute Instance","quantity":1,"unitPrice":1200.00,"total":1200.00},{"itemName":"Tax Adjustment","quantity":1,"unitPrice":240.00,"total":240.00}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "groq");

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Acme Cloud Ltd.", result.Value.VendorName);
        Assert.Equal("groq", result.Value.Source);
        Assert.Equal(2, result.Value.LineItems.Count);
    }

    [Fact]
    public void Parse_ReturnsResult_ForFencedJson()
    {
        const string json =
            """
            ```json
            {"vendorName":"OpenRouter Vendor","reference":"INV-2026-1188","documentDate":"2026-04-20","dueDate":"2026-05-05","category":"Marketing","vendorTaxId":"TX-789","subtotal":900.00,"vat":90.00,"totalAmount":990.00,"lineItems":[{"itemName":"Campaign Creative","quantity":1,"unitPrice":900.00,"total":900.00},{"itemName":"VAT","quantity":1,"unitPrice":90.00,"total":90.00}]}
            ```
            """;

        var result = LlmVisionOcrParser.Parse(json, "openrouter");

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("OpenRouter Vendor", result.Value.VendorName);
        Assert.Equal("openrouter", result.Value.Source);
    }

    [Fact]
    public void Parse_ReturnsInvalidJson_WhenRequiredFieldMissing()
    {
        const string json =
            """
            {"reference":"INV-2026-0042","documentDate":"2026-04-18","dueDate":"2026-05-02","category":"Software & SaaS","vendorTaxId":"TX-123","subtotal":1200.00,"vat":240.00,"totalAmount":1440.00,"lineItems":[{"itemName":"Cloud Compute Instance","quantity":1,"unitPrice":1200.00,"total":1200.00}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "groq");

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrInvalidJson, result.Error);
    }

    [Fact]
    public void Parse_ReturnsInvalidJson_WhenNumericTypeIsWrong()
    {
        const string json =
            """
            {"vendorName":"Acme Cloud Ltd.","reference":"INV-2026-0042","documentDate":"2026-04-18","dueDate":"2026-05-02","category":"Software & SaaS","vendorTaxId":"TX-123","subtotal":"1200.00","vat":240.00,"totalAmount":1440.00,"lineItems":[{"itemName":"Cloud Compute Instance","quantity":1,"unitPrice":1200.00,"total":1200.00},{"itemName":"Tax Adjustment","quantity":1,"unitPrice":240.00,"total":240.00}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "groq");

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrInvalidJson, result.Error);
    }

    [Fact]
    public void Parse_UsesFallbacks_WhenDueDateAndCategoryAreNull()
    {
        const string json =
            """
            {"vendorName":"BIG C DI AN","reference":"P Dong Hoa, TX Di An, Tinh Binh Duong","documentDate":"2018-10-02","dueDate":null,"category":null,"vendorTaxId":"3702058398","subtotal":17000,"vat":0,"totalAmount":0,"lineItems":[{"itemName":"SCU TT NUTI DAU 11","quantity":2,"unitPrice":17000,"total":17000},{"itemName":"SCU TT NUTI DAU 110M","quantity":1,"unitPrice":-17000,"total":-17000}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "openrouter");

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal(new DateOnly(2018, 10, 02), result.Value.DocumentDate);
        Assert.Equal(new DateOnly(2018, 10, 02), result.Value.DueDate);
        Assert.Equal("Uncategorized", result.Value.Category);
        Assert.Equal(0m, result.Value.TotalAmount);
        Assert.Equal(2, result.Value.LineItems.Count);
    }
}
