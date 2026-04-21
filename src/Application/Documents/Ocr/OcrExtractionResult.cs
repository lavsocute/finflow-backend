namespace FinFlow.Application.Documents.Ocr;

public sealed record OcrExtractionResult(
    string VendorName,
    string Reference,
    DateOnly DocumentDate,
    DateOnly DueDate,
    string Category,
    string? VendorTaxId,
    decimal Subtotal,
    decimal Vat,
    decimal TotalAmount,
    string Source,
    string ConfidenceLabel,
    IReadOnlyList<OcrExtractionLineItem> LineItems,
    int ProcessedPageCount,
    IReadOnlyList<string> Warnings = null!
)
{
    public static OcrExtractionResult Create(
        string vendorName,
        string reference,
        DateOnly documentDate,
        DateOnly dueDate,
        string category,
        string? vendorTaxId,
        decimal subtotal,
        decimal vat,
        decimal totalAmount,
        string source,
        string confidenceLabel,
        IReadOnlyList<OcrExtractionLineItem> lineItems,
        int processedPageCount,
        bool wasTruncated = false)
    {
        var warnings = new List<string>();
        if (wasTruncated)
            warnings.Add($"Document was truncated. Only {processedPageCount} page(s) were processed.");

        return new OcrExtractionResult(
            vendorName,
            reference,
            documentDate,
            dueDate,
            category,
            vendorTaxId,
            subtotal,
            vat,
            totalAmount,
            source,
            confidenceLabel,
            lineItems,
            processedPageCount,
            warnings);
    }
}
