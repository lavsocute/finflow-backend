using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FinFlow.Application.Chat.Services;

internal static partial class IntentTextNormalizer
{
    private static readonly (Regex Pattern, string Replacement)[] PhraseCorrections =
    [
        (BurnHnPattern(), "burn hon"),
        (NganSachsPattern(), "ngan sach"),
        (DotTienVPattern(), "dot tien vay"),
        (BurnZPattern(), "burn vay")
    ];

    private static readonly (Regex Pattern, string Replacement)[] TokenCorrections =
    [
        (SpenPattern(), "spend"),
        (SpemdPattern(), "spend"),
        (BurmPattern(), "burn"),
        (VedorPattern(), "vendor")
    ];

    public static string Normalize(string query)
    {
        var withoutDiacritics = RemoveDiacritics(query);
        var normalized = withoutDiacritics.ToLowerInvariant();

        foreach (var (pattern, replacement) in PhraseCorrections)
            normalized = pattern.Replace(normalized, replacement);

        foreach (var (pattern, replacement) in TokenCorrections)
            normalized = pattern.Replace(normalized, replacement);

        normalized = WhitespacePattern().Replace(normalized, " ").Trim();
        return normalized;
    }

    private static string RemoveDiacritics(string query)
    {
        var normalized = query.Normalize(NormalizationForm.FormD);
        Span<char> buffer = stackalloc char[normalized.Length];
        var index = 0;

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            buffer[index++] = character switch
            {
                'đ' => 'd',
                'Đ' => 'd',
                _ => character
            };
        }

        return new string(buffer[..index]).Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\bburn\s+hn\b", RegexOptions.IgnoreCase)]
    private static partial Regex BurnHnPattern();

    [GeneratedRegex(@"\bngan\s+sachs\b", RegexOptions.IgnoreCase)]
    private static partial Regex NganSachsPattern();

    [GeneratedRegex(@"\bdot\s+tien\s+v\b", RegexOptions.IgnoreCase)]
    private static partial Regex DotTienVPattern();

    [GeneratedRegex(@"\bburn\s+z\b", RegexOptions.IgnoreCase)]
    private static partial Regex BurnZPattern();

    [GeneratedRegex(@"\bspen\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpenPattern();

    [GeneratedRegex(@"\bspemd\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpemdPattern();

    [GeneratedRegex(@"\bburm\b", RegexOptions.IgnoreCase)]
    private static partial Regex BurmPattern();

    [GeneratedRegex(@"\bvedor\b", RegexOptions.IgnoreCase)]
    private static partial Regex VedorPattern();

    [GeneratedRegex(@"\s+", RegexOptions.IgnoreCase)]
    private static partial Regex WhitespacePattern();
}
