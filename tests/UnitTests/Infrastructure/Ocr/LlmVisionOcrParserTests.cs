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
            {"vendorName":"Acme Cloud Ltd.","reference":"INV-2026-0042","documentDate":"2026-04-18","extractedInvoiceDueDate":"2026-05-02","category":"Software & SaaS","vendorTaxId":"TX-123","subtotal":1200.00,"vat":240.00,"totalAmount":1440.00,"lineItems":[{"itemName":"Cloud Compute Instance","quantity":1,"unitPrice":1200.00,"total":1200.00},{"itemName":"Tax Adjustment","quantity":1,"unitPrice":240.00,"total":240.00}]}
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
            {"vendorName":"OpenRouter Vendor","reference":"INV-2026-1188","documentDate":"2026-04-20","extractedInvoiceDueDate":"2026-05-05","category":"Marketing","vendorTaxId":"TX-789","subtotal":900.00,"vat":90.00,"totalAmount":990.00,"lineItems":[{"itemName":"Campaign Creative","quantity":1,"unitPrice":900.00,"total":900.00},{"itemName":"VAT","quantity":1,"unitPrice":90.00,"total":90.00}]}
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
            {"reference":"INV-2026-0042","documentDate":"2026-04-18","extractedInvoiceDueDate":"2026-05-02","category":"Software & SaaS","vendorTaxId":"TX-123","subtotal":1200.00,"vat":240.00,"totalAmount":1440.00,"lineItems":[{"itemName":"Cloud Compute Instance","quantity":1,"unitPrice":1200.00,"total":1200.00}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "groq");

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentOcrErrors.OcrInvalidJson, result.Error);
    }

    [Fact]
    public void Parse_AcceptsNumericStrings_FromLlmResponses()
    {
        const string json =
            """
            {"vendorName":"Acme Cloud Ltd.","reference":"INV-2026-0042","documentDate":"2026-04-18","extractedInvoiceDueDate":"2026-05-02","category":"Software & SaaS","vendorTaxId":"TX-123","subtotal":"1200.00","vat":240.00,"totalAmount":1440.00,"lineItems":[{"itemName":"Cloud Compute Instance","quantity":1,"unitPrice":1200.00,"total":1200.00},{"itemName":"Tax Adjustment","quantity":1,"unitPrice":240.00,"total":240.00}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "groq");

        Assert.True(result.IsSuccess);
        Assert.Equal(1200.00m, result.Value.Subtotal);
    }

    [Fact]
    public void Parse_ReturnsTaxLines_WhenLlmExtractsVatRateAndAmount()
    {
        const string json =
            """
            {"vendorName":"Acme Cloud Ltd.","reference":"INV-2026-0042","documentDate":"2026-04-18","extractedInvoiceDueDate":"2026-05-02","category":"Software & SaaS","vendorTaxId":"TX-123","subtotal":1200.00,"vat":120.00,"totalAmount":1320.00,"taxLines":[{"taxType":"VAT","rate":10.00,"taxableAmount":1200.00,"taxAmount":120.00}],"lineItems":[{"itemName":"Cloud Compute Instance","quantity":1,"unitPrice":1200.00,"total":1200.00}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "groq");

        Assert.True(result.IsSuccess, result.Error.Description);
        var taxLine = Assert.Single(result.Value.TaxLines);
        Assert.Equal("VAT", taxLine.TaxType);
        Assert.Equal(10.00m, taxLine.Rate);
        Assert.Equal(1200.00m, taxLine.TaxableAmount);
        Assert.Equal(120.00m, taxLine.TaxAmount);
    }

    [Fact]
    public void Parse_ReturnsLineItemTaxFields_WhenLlmExtractsMultiRateVat()
    {
        const string json =
            """
            {"vendorName":"Co.op Mart","reference":"COOP-2026-0001","documentDate":"2026-05-27","extractedInvoiceDueDate":null,"category":"Groceries","vendorTaxId":"0312345678","subtotal":300000.00,"vat":25000.00,"totalAmount":325000.00,"taxLines":[{"taxType":"VAT","rate":5.00,"taxableAmount":100000.00,"taxAmount":5000.00},{"taxType":"VAT","rate":10.00,"taxableAmount":200000.00,"taxAmount":20000.00}],"lineItems":[{"itemName":"Fresh food","quantity":1,"unitPrice":100000.00,"taxRate":5.00,"taxableAmount":100000.00,"taxAmount":5000.00,"total":100000.00},{"itemName":"Household item","quantity":1,"unitPrice":200000.00,"taxRate":10.00,"taxableAmount":200000.00,"taxAmount":20000.00,"total":200000.00}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "groq");

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal(2, result.Value.TaxLines.Count);
        Assert.Equal(5.00m, result.Value.LineItems[0].TaxRate);
        Assert.Equal(100000.00m, result.Value.LineItems[0].TaxableAmount);
        Assert.Equal(5000.00m, result.Value.LineItems[0].TaxAmount);
        Assert.Equal(10.00m, result.Value.LineItems[1].TaxRate);
        Assert.Equal(200000.00m, result.Value.LineItems[1].TaxableAmount);
        Assert.Equal(20000.00m, result.Value.LineItems[1].TaxAmount);
    }

    [Fact]
    public void Parse_Leaves_ExtractedInvoiceDueDate_Null_WhenMissing_AndUsesCategoryFallback()
    {
        const string json =
            """
            {"vendorName":"BIG C DI AN","reference":"P Dong Hoa, TX Di An, Tinh Binh Duong","documentDate":"2018-10-02","extractedInvoiceDueDate":null,"category":null,"vendorTaxId":"3702058398","subtotal":17000,"vat":0,"totalAmount":0,"lineItems":[{"itemName":"SCU TT NUTI DAU 11","quantity":2,"unitPrice":17000,"total":17000},{"itemName":"SCU TT NUTI DAU 110M","quantity":1,"unitPrice":-17000,"total":-17000}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "openrouter");

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal(new DateOnly(2018, 10, 02), result.Value.DocumentDate);
        Assert.Null(result.Value.ExtractedInvoiceDueDate);
        Assert.Equal("Uncategorized", result.Value.Category);
        Assert.Equal(0m, result.Value.TotalAmount);
        Assert.Equal(2, result.Value.LineItems.Count);
    }

    [Fact]
    public void Parse_NormalizesBusinessText_ForVendorReferenceAndLineItems()
    {
        const string json =
            """
            {"vendorName":"  BACH HOA XANH  ","reference":" inv / 2026 / 0042 ","documentDate":"2026-04-18","extractedInvoiceDueDate":null,"category":"  SOFTWARE   &   SAAS ","vendorTaxId":"TX-123","subtotal":1200.00,"vat":240.00,"totalAmount":1440.00,"lineItems":[{"itemName":"  CLOUD    COMPUTE   INSTANCE  ","quantity":1,"unitPrice":1200.00,"total":1200.00}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "groq");

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Bach Hoa Xanh", result.Value.VendorName);
        Assert.Equal("INV/2026/0042", result.Value.Reference);
        Assert.Equal("SOFTWARE & SAAS", result.Value.Category);
        Assert.Equal("CLOUD COMPUTE INSTANCE", result.Value.LineItems[0].ItemName);
    }

    [Fact]
    public void Parse_RepairsCommonUtf8Mojibake_InVendorName()
    {
        const string json =
            """
            {"vendorName":"BÃ¡ch HÃ³a Xanh","reference":"inv-2026-0042","documentDate":"2026-04-18","extractedInvoiceDueDate":null,"category":"Software","vendorTaxId":"TX-123","subtotal":1200.00,"vat":240.00,"totalAmount":1440.00,"lineItems":[{"itemName":"VAT","quantity":1,"unitPrice":240.00,"total":240.00}]}
            """;

        var result = LlmVisionOcrParser.Parse(json, "groq");

        Assert.True(result.IsSuccess, result.Error.Description);
        Assert.Equal("Bách Hóa Xanh", result.Value.VendorName);
    }
}
