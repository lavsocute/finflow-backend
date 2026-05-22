using System.Text.RegularExpressions;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Documents;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Extracts [chunk-N] markers from an LLM response and maps them back to the
/// source chunks. Markers are 1-indexed to match the JSON evidence payload.
/// </summary>
public static partial class ChatCitationParser
{
    [GeneratedRegex(@"\[chunk-(\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex ChunkMarkerRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex MultiWhitespaceRegex();

    public static IReadOnlyList<ChatCitation> Parse(string response, IReadOnlyList<DocumentChunk> chunks)
    {
        if (string.IsNullOrEmpty(response) || chunks.Count == 0)
            return Array.Empty<ChatCitation>();

        var seen = new HashSet<int>();
        var citations = new List<ChatCitation>();

        foreach (Match match in ChunkMarkerRegex().Matches(response))
        {
            if (!int.TryParse(match.Groups[1].Value, out var n)) continue;
            if (n < 1 || n > chunks.Count) continue;
            if (!seen.Add(n)) continue;

            var chunk = chunks[n - 1];
            var preview = chunk.Content.Length <= 100
                ? chunk.Content
                : chunk.Content[..100] + "...";

            citations.Add(new ChatCitation(
                ChunkNumber: n,
                ChunkId: chunk.Id,
                DocumentId: chunk.DocumentId,
                ChunkType: chunk.Type.ToString(),
                Preview: preview));
        }

        return citations;
    }

    public static string StripMarkers(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return response;

        var stripped = ChunkMarkerRegex().Replace(response, string.Empty);
        stripped = Regex.Replace(stripped, @"\s+([.,;:!?])", "$1");
        stripped = MultiWhitespaceRegex().Replace(stripped, " ");
        return stripped.Trim();
    }
}
