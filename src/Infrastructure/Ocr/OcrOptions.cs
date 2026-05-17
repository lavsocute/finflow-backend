namespace FinFlow.Infrastructure.Ocr;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    public string ActiveProvider { get; init; } = "Groq";

    /// <summary>
    /// Ordered fallback chain. If the primary provider fails (timeout, error),
    /// each fallback is tried in turn before giving up. Null/empty = no fallback.
    /// </summary>
    public string[] ProviderFallbackChain { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Per-provider timeout. Beyond this, fall back to next in chain.
    /// </summary>
    public int ProviderTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// When true, OCR results are cached by content hash to skip re-extraction
    /// of identical files. TTL: 1 hour.
    /// </summary>
    public bool EnableContentHashCache { get; init; } = true;
}

