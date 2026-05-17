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
public sealed partial class RegexContentModerator : IContentModerator
{
    [GeneratedRegex(@"(?i)\b(kill|murder|stab|shoot|bomb|behead|destroy)\s+(you|him|her|them|all)\b")]
    private static partial Regex ThreatRegex();

    [GeneratedRegex(@"(?i)\b(porn|xxx|nude|naked|sex(?:ual)?\s+(?:content|act)|child\s+(?:porn|abuse))\b")]
    private static partial Regex NsfwRegex();

    [GeneratedRegex(@"(?i)\b(retard|nigger|chink|spic|kike|faggot|tranny)\b")]
    private static partial Regex HateRegex();

    // Vietnamese profanity / threat indicators (subset).
    [GeneratedRegex(@"(?i)(giết\s+(mày|nó|chúng)|đập\s+chết|đâm\s+chết|làm\s+nhục)")]
    private static partial Regex VietnameseThreatRegex();

    public string? Moderate(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        if (ThreatRegex().IsMatch(query)) return "threat";
        if (VietnameseThreatRegex().IsMatch(query)) return "threat";
        if (NsfwRegex().IsMatch(query)) return "nsfw";
        if (HateRegex().IsMatch(query)) return "hate";

        return null;
    }
}
