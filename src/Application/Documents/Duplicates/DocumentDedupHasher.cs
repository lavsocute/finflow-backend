using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FinFlow.Application.Documents.Duplicates;

/// <summary>
/// Computes a deterministic 32-char hex hash for a receipt's natural identity:
/// (vendor, invoice number, date, total). Two documents within a tenant that
/// produce the same hash within the 90-day sliding window are flagged as
/// potential duplicates.
///
/// Hash inputs are normalized — case-folded, whitespace-stripped, locale-fixed —
/// so OCR jitter (vd: "INV  001 " vs "INV-001") doesn't defeat detection.
/// </summary>
public static class DocumentDedupHasher
{
    /// <summary>
    /// Build the dedup hash. Returns null when the inputs are too sparse to
    /// reliably identify a unique receipt (vd: missing both tax id AND invoice
    /// number) — in that case the caller should NOT enroll the document into
    /// dedup detection (false positives high).
    /// </summary>
    public static string? Compute(
        string? vendorTaxId,
        string vendorName,
        string invoiceNumber,
        DateOnly documentDate,
        decimal totalAmount)
    {
        var vendorKey = NormalizeVendorKey(vendorTaxId, vendorName);
        var invoiceKey = NormalizeInvoice(invoiceNumber);
        if (string.IsNullOrEmpty(vendorKey) || string.IsNullOrEmpty(invoiceKey))
            return null;

        var input = string.Join('|',
            vendorKey,
            invoiceKey,
            documentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            totalAmount.ToString("0.00", CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 16).ToUpperInvariant();
    }

    private static string NormalizeVendorKey(string? taxId, string vendorName)
    {
        // Prefer tax id (more stable than name OCR).
        if (!string.IsNullOrWhiteSpace(taxId))
            return $"TAX:{taxId.Trim().ToUpperInvariant()}";

        if (string.IsNullOrWhiteSpace(vendorName))
            return string.Empty;

        // Strip diacritics + non-alphanumerics so VN "Cty TNHH ABC" matches
        // OCR "CTY TNHH ABC,".
        var stripped = StripDiacritics(vendorName.Trim()).ToUpperInvariant();
        var sb = new StringBuilder(stripped.Length);
        foreach (var c in stripped)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return $"NAME:{sb}";
    }

    private static string NormalizeInvoice(string invoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            return string.Empty;

        var sb = new StringBuilder(invoiceNumber.Length);
        foreach (var c in invoiceNumber)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    private static string StripDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormC);
    }
}
