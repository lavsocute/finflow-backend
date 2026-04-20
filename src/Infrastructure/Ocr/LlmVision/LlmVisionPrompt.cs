namespace FinFlow.Infrastructure.Ocr.LlmVision;

public static class LlmVisionPrompt
{
    public const string ExtractExpenseDocument =
        """
        Extract this expense document into strict JSON only.
        Return keys:
        vendorName, reference, documentDate, dueDate, category, vendorTaxId, subtotal, vat, totalAmount, lineItems.
        lineItems must be an array of objects with itemName, quantity, unitPrice, total.
        Rules:
        - Output JSON only
        - Dates must use yyyy-MM-dd
        - Use numbers for money and quantities
        - Do not invent values
        """;
}
