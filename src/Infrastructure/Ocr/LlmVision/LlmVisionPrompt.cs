namespace FinFlow.Infrastructure.Ocr.LlmVision;

public static class LlmVisionPrompt
{
    public const string ExtractExpenseDocument =
        """
        Extract this expense document into strict JSON only.
        Return keys:
        vendorName, reference, documentDate, extractedInvoiceDueDate, category, vendorTaxId,
        currencyCode, subtotal, vat, totalAmount, taxLines, lineItems.
        taxLines must be an array grouped by tax rate with taxType, rate, taxableAmount, taxAmount.
        lineItems must be an array of objects with itemName, quantity, unitPrice, discountAmount, taxRate, taxableAmount, taxAmount, total.
        category must be one of these exact values:
        "Văn phòng phẩm", "Đi lại & Vận chuyển", "Ăn uống & Tiếp khách",
        "Công nghệ & Phần mềm", "Thiết bị & Dụng cụ", "Thuê mặt bằng & Tiện ích",
        "Marketing & Quảng cáo", "Đào tạo & Phát triển", "Bảo hiểm",
        "Dịch vụ chuyên môn", "Nguyên vật liệu", "Bảo trì & Sửa chữa",
        "Vận hành sản xuất", "Phúc lợi nhân viên", "Thuế & Phí", "Khác".
        Pick the closest match based on the document content. If unsure, use "Khác".
        Rules:
        - Output JSON only.
        - Dates must use yyyy-MM-dd.
        - Use numbers for money and quantities (no thousand separators).
        - Keep lineItems.total as the tax-exclusive net amount after any line discount.
        - When a line's VAT rate is visible, return taxRate, taxableAmount, and taxAmount on that line.
        - If line VAT is not visible, use taxRate null, taxableAmount 0, and taxAmount 0.
        - taxLines must summarize VAT by rate. If there is no VAT, return an empty array.
        - currencyCode must be a 3-letter ISO 4217 code in uppercase (USD, VND, EUR, GBP, JPY, SGD, CNY...).
          Infer it from currency symbols or words on the document. If genuinely unknown, return null.
          Do NOT default to a region's currency; only return a code you can verify on the page.
        - Do not invent values. Use null for fields you cannot read.
        """;
}
