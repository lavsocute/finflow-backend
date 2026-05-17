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
        sanitized = ApplyRegex(sanitized, PhoneRegex(), "Phone", redactions, ref totalCount);
        sanitized = ApplyRegex(sanitized, TaxIdRegex(), "TaxId", redactions, ref totalCount);
        sanitized = ApplyRegex(sanitized, BankAccountRegex(), "BankAccount", redactions, ref totalCount);
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
}
