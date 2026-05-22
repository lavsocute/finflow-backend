using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FinFlow.Application.Documents.Ocr;

public static partial class DocumentTextNormalizer
{
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiWhitespaceRegex();

    public static string NormalizeVendorName(string value)
    {
        var normalized = NormalizeWhitespace(TryRepairUtf8Mojibake(value));
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (ShouldTitleCase(normalized))
            return ToTitleCase(normalized);

        return normalized;
    }

    public static string NormalizeReference(string value)
    {
        var normalized = NormalizeWhitespace(TryRepairUtf8Mojibake(value));
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        normalized = normalized
            .Replace(" - ", "-", StringComparison.Ordinal)
            .Replace(" / ", "/", StringComparison.Ordinal)
            .Replace(" _ ", "_", StringComparison.Ordinal)
            .Replace(" . ", ".", StringComparison.Ordinal);

        return normalized.ToUpperInvariant();
    }

    public static string NormalizeCategory(string value)
    {
        return NormalizeWhitespace(TryRepairUtf8Mojibake(value));
    }

    public static string NormalizeLineItemName(string value)
    {
        return NormalizeWhitespace(TryRepairUtf8Mojibake(value));
    }

    public static string NormalizeEvidenceValue(string value) =>
        NormalizeWhitespace(TryRepairUtf8Mojibake(value));

    public static string BuildSearchKey(string value)
    {
        var normalized = StripDiacritics(NormalizeEvidenceValue(value)).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }

        return builder.ToString();
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return MultiWhitespaceRegex().Replace(value.Trim(), " ");
    }

    private static string TryRepairUtf8Mojibake(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !LooksLikeUtf8Mojibake(value))
            return value;

        try
        {
            return Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(value));
        }
        catch (DecoderFallbackException)
        {
            return value;
        }
    }

    private static bool LooksLikeUtf8Mojibake(string value) =>
        value.Contains('Ã') ||
        value.Contains('Â') ||
        value.Contains('Ä') ||
        value.Contains('Å') ||
        value.Contains("á»", StringComparison.Ordinal) ||
        value.Contains("áº", StringComparison.Ordinal);

    private static bool ShouldTitleCase(string value)
    {
        var hasLetter = false;
        var hasLower = false;
        var hasUpper = false;

        foreach (var character in value)
        {
            if (!char.IsLetter(character))
                continue;

            hasLetter = true;
            if (char.IsLower(character))
                hasLower = true;
            if (char.IsUpper(character))
                hasUpper = true;
        }

        return hasLetter && hasUpper && !hasLower;
    }

    private static string ToTitleCase(string value)
    {
        var lower = value.ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
    }

    private static string StripDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder
            .ToString()
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .Normalize(NormalizationForm.FormC);
    }
}
