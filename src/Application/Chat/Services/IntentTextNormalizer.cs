using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Static text normalizer for use in static contexts where DI is not available.
/// Delegates to the singleton ITextNormalizer instance.
/// </summary>
public static class IntentTextNormalizer
{
    private static readonly ITextNormalizer _normalizer = new TextNormalizer();

    public static string Normalize(string query) => _normalizer.Normalize(query);
}