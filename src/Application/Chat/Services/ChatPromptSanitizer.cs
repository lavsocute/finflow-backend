using System.Globalization;
using System.Text;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Sanitizes free-form text before it is interpolated into LLM prompts.
/// Hardens against Unicode tricks (zero-width / bidi), common instruction labels
/// across multiple languages, and indirect injection via document content.
/// </summary>
public static class ChatPromptSanitizer
{
    private static readonly char[] ZeroWidthChars =
    [
        '\u200B', // ZERO WIDTH SPACE
        '\u200C', // ZERO WIDTH NON-JOINER
        '\u200D', // ZERO WIDTH JOINER
        '\uFEFF'  // ZERO WIDTH NO-BREAK SPACE / BOM
    ];

    private static readonly char[] BidiControlChars =
    [
        '\u202A', // LEFT-TO-RIGHT EMBEDDING
        '\u202B', // RIGHT-TO-LEFT EMBEDDING
        '\u202C', // POP DIRECTIONAL FORMATTING
        '\u202D', // LEFT-TO-RIGHT OVERRIDE
        '\u202E', // RIGHT-TO-LEFT OVERRIDE
        '\u2066', // LEFT-TO-RIGHT ISOLATE
        '\u2067', // RIGHT-TO-LEFT ISOLATE
        '\u2068', // FIRST STRONG ISOLATE
        '\u2069'  // POP DIRECTIONAL ISOLATE
    ];

    // Multi-language instruction labels. Listed lowercase; matching is case-insensitive.
    private static readonly string[] InstructionLabels =
    [
        "system:",
        "assistant:",
        "user:",
        "developer:",
        "tool:",
        // Vietnamese
        "hệ thống:",
        "trợ lý:",
        "người dùng:",
        // Spanish / French / Italian / Portuguese
        "sistema:",
        "système:",
        "asistente:",
        "asistente:",
        "utilisateur:",
        // German / Dutch
        "systeem:",
        "benutzer:",
        // Russian
        "система:",
        "пользователь:",
        // Chinese (simplified + traditional)
        "系统:",
        "系統:",
        "助手:",
        "用户:",
        "用戶:",
        // Korean
        "시스템:",
        "어시스턴트:",
        "사용자:",
        // Japanese
        "システム:",
        "アシスタント:",
        "ユーザー:"
    ];

    /// <summary>
    /// Apply NFKC + zero-width strip + bidi strip + label neutralization.
    /// Safe for empty/null input. Returns empty for null.
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Step 1: Unicode NFKC normalization. Catches half-width/full-width tricks,
        // Cyrillic look-alikes that have NFKC mappings, etc.
        var normalized = input.Normalize(NormalizationForm.FormKC);

        // Step 2: Strip zero-width and bidi control characters.
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (Array.IndexOf(ZeroWidthChars, ch) >= 0) continue;
            if (Array.IndexOf(BidiControlChars, ch) >= 0) continue;
            // Fix #16: also strip Arabic Letter Mark and Variation Selectors (FE00-FE0F)
            if (ch == '\u061C') continue;
            if (ch >= '\uFE00' && ch <= '\uFE0F') continue;
            // Other control characters stripped (except tab, LF, CR for prompts).
            if (char.IsControl(ch) && ch != '\t' && ch != '\n' && ch != '\r') continue;
            sb.Append(ch);
        }
        // Strip Unicode Tag block (E0000-E007F) — used for invisible-tag prompt injection.
        var stripped = StripTagBlock(sb.ToString());

        // Step 3: Neutralize instruction-style labels regardless of language/case.
        // Fix #15: advance index by replacement length, not original label length,
        // so we don't re-scan inside the inserted "[label]" marker.
        var lower = stripped.ToLowerInvariant();
        foreach (var label in InstructionLabels)
        {
            int idx = 0;
            while ((idx = lower.IndexOf(label, idx, StringComparison.Ordinal)) >= 0)
            {
                var replacement = $"[{label.TrimEnd(':')} label]";
                stripped = stripped.Remove(idx, label.Length).Insert(idx, replacement);
                lower = stripped.ToLowerInvariant();
                idx += replacement.Length;
                if (idx >= lower.Length) break;
            }
        }

        return stripped;
    }

    /// <summary>
    /// Remove Unicode Tag block (U+E0000–U+E007F) which can carry invisible instructions.
    /// Iterates by Rune to handle surrogate pairs correctly.
    /// </summary>
    private static string StripTagBlock(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; )
        {
            if (System.Text.Rune.TryGetRuneAt(input, i, out var rune))
            {
                if (rune.Value >= 0xE0000 && rune.Value <= 0xE007F)
                {
                    i += rune.Utf16SequenceLength;
                    continue;
                }
                sb.Append(input, i, rune.Utf16SequenceLength);
                i += rune.Utf16SequenceLength;
            }
            else
            {
                // Lone surrogate — skip (also a defense against malformed Unicode).
                i++;
            }
        }
        return sb.ToString();
    }
}
