using System.Text.RegularExpressions;
using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Phase-1 content moderator. Conservative regex blacklist for
/// English + Vietnamese hate / threat / NSFW signals.
///
/// NOTE: this is intentionally simple and biased toward false-positive.
/// Phase-2 should swap to Llama Guard or similar model-based classifier.
/// </summary>
public sealed class RegexContentModerator : IContentModerator
{
    private static readonly Regex ThreatRegex = new Regex(
        @"(?i)\b(kill|murder|stab|shoot|bomb|behead|destroy)\s+(you|him|her|them|all)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NsfwRegex = new Regex(
        @"(?i)\b(porn|xxx|nude|naked|sex(?:ual)?\s+(?:content|act)|child\s+(?:porn|abuse))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HateRegex = new Regex(
        @"(?i)\b(retard|nigger|chink|spic|kike|faggot|tranny)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VietnameseThreatRegex = new Regex(
        @"(?i)(giết\s+(mày|nó|chúng)|đập\s+chết|đâm\s+chết|làm\s+nhục)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string? Moderate(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        if (ThreatRegex.IsMatch(query)) return "threat";
        if (VietnameseThreatRegex.IsMatch(query)) return "threat";
        if (NsfwRegex.IsMatch(query)) return "nsfw";
        if (HateRegex.IsMatch(query)) return "hate";

        return null;
    }
}
