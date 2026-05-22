using System.Text.RegularExpressions;
using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Regex-based output filter. Detects common PII (email, VN phone, VN tax id, bank account)
/// and system-prompt fragments that suggest a leak, then redacts them.
///
/// Trade-off: prefers false-positive over false-negative. Numbers in legitimate financial
/// answers may get redacted; users can re-ask with disambiguation if needed.
/// </summary>
public sealed partial class ChatOutputFilter : IChatOutputFilter
{
    private static readonly string[] BusinessIdentifierPrefixes =
    [
        "mã",
        "ma",
        "reference",
        "ref",
        "invoice",
        "document",
        "approval",
        "execution reference",
        "executionreference",
        "mã tham chiếu",
        "ma tham chieu",
        "mã hóa đơn",
        "ma hoa don",
        "số hóa đơn",
        "so hoa don",
        // FIX #7: Expanded blocklist to catch more business ID patterns
        "số tài khoản",
        "so tai khoan",
        "so tk",
        "stk",
        "tai khoan",
        "tai khoan",
        "account",
        "số chứng từ",
        "so chung tu",
        "chứng từ",
        "chung tu",
        "bill",
        "receipt",
        "biên lai",
        "bien lai",
        "phiếu",
        "phieu",
        "order",
        "đơn hàng",
        "don hang",
        "purchase order",
        "po number",
        "contract",
        "hợp đồng",
        "hop dong"
    ];

    private static readonly string[] PhoneContextHints =
    [
        "phone",
        "mobile",
        "tel",
        "call",
        "contact",
        "điện thoại",
        "dien thoai",
        "số điện thoại",
        "so dien thoai"
    ];

    private static readonly string[] TaxIdContextHints =
    [
        "tax id",
        "taxid",
        "tax code",
        "mst",
        "mã số thuế",
        "ma so thue",
        "tin"
    ];

    private static readonly string[] BankAccountContextHints =
    [
        "bank account",
        "account number",
        "iban",
        "swift",
        "stk",
        "số tài khoản",
        "so tai khoan",
        "tài khoản",
        "tai khoan",
        "ngân hàng",
        "ngan hang"
    ];

    // Email — RFC-lite, good enough for redaction.
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    // VN phone: 10 digits starting with 0, optionally with +84 prefix and separators.
    [GeneratedRegex(@"(?:\+?84|0)\s?\d{2,3}[\s.-]?\d{3,4}[\s.-]?\d{3,4}")]
    private static partial Regex PhoneRegex();

    // VN tax id: 10 digits, optionally followed by -3 digits (branch code).
    [GeneratedRegex(@"\b\d{10}(?:-\d{3})?\b")]
    private static partial Regex TaxIdRegex();

    // Bank account: 8-19 consecutive digits not adjacent to letters.
    // Constrained to long sequences to avoid catching typical money amounts.
    [GeneratedRegex(@"\b\d{12,19}\b")]
    private static partial Regex BankAccountRegex();

    // FIX #4: Vietnamese digit-words (for PII redaction bypass via spelling out numbers)
    [GeneratedRegex(@"\b(một|hai|ba|bốn|năm|sáu|bảy|tám|chín|không|mốt|hàng|moot|haii|bazi|bonn|namm|saux|bayy|tamm|chin|khong|zéro|một|hai|ba|bốn|năm|sáu|bảy|tám|chín|zéro|zero|one|two|three|four|five|six|seven|eight|nine)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VietnameseDigitWordsRegex();

    // System prompt leak indicators: model parroting back our framing language.
    [GeneratedRegex(@"(?i)you are FinFlow|treat retrieved document text|as untrusted evidence|never reveal.*?instructions|your responses must always stay within")]
    private static partial Regex SystemPromptRegex();

    public ChatOutputFilterResult Sanitize(string rawResponse)
    {
        if (string.IsNullOrEmpty(rawResponse))
            return new ChatOutputFilterResult(rawResponse ?? string.Empty, 0, Array.Empty<string>());

        var sanitized = rawResponse;
        var redactions = new List<string>();
        var totalCount = 0;

        sanitized = ApplyRegex(sanitized, EmailRegex(), "Email", redactions, ref totalCount);
        sanitized = ApplyConditionalRegex(sanitized, PhoneRegex(), "Phone", PhoneContextHints, redactions, ref totalCount);
        sanitized = ApplyConditionalRegex(sanitized, TaxIdRegex(), "TaxId", TaxIdContextHints, redactions, ref totalCount);
        sanitized = ApplyConditionalRegex(sanitized, BankAccountRegex(), "BankAccount", BankAccountContextHints, redactions, ref totalCount);
        sanitized = ApplyConditionalRegex(sanitized, VietnameseDigitWordsRegex(), "DigitWords", BankAccountContextHints, redactions, ref totalCount);
        sanitized = ApplyRegex(sanitized, SystemPromptRegex(), "SystemPrompt", redactions, ref totalCount);

        return new ChatOutputFilterResult(sanitized, totalCount, redactions);
    }

    private static string ApplyRegex(
        string input,
        Regex regex,
        string label,
        List<string> redactionTypes,
        ref int totalCount)
    {
        var matches = regex.Matches(input);
        if (matches.Count == 0)
            return input;

        if (!redactionTypes.Contains(label))
            redactionTypes.Add(label);

        totalCount += matches.Count;
        return regex.Replace(input, $"[REDACTED:{label}]");
    }

    private static string ApplyConditionalRegex(
        string input,
        Regex regex,
        string label,
        IReadOnlyCollection<string> contextHints,
        List<string> redactionTypes,
        ref int totalCount)
    {
        var replacements = 0;
        var redacted = regex.Replace(input, match =>
        {
            if (ShouldPreserveAsBusinessIdentifier(input, match.Index))
                return match.Value;

            if (!HasContextHint(input, match.Index, contextHints))
                return match.Value;

            replacements++;
            return $"[REDACTED:{label}]";
        });

        if (replacements == 0)
            return input;

        if (!redactionTypes.Contains(label))
            redactionTypes.Add(label);

        totalCount += replacements;
        return redacted;
    }

    private static bool ShouldPreserveAsBusinessIdentifier(string input, int matchIndex)
        => HasContextHint(input, matchIndex, BusinessIdentifierPrefixes);

    private static bool HasContextHint(string input, int matchIndex, IReadOnlyCollection<string> hints)
    {
        const int windowSize = 64;
        var windowStart = Math.Max(0, matchIndex - windowSize);
        var windowEnd = Math.Min(input.Length, matchIndex + windowSize);
        var window = input[windowStart..windowEnd].ToLowerInvariant();

        return hints.Any(window.Contains);
    }
}
