using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Stateful streaming filter wrapper. Buffers a sliding window of tokens so PII
/// patterns (which span multiple tokens) can be detected before bytes hit the wire.
///
/// Strategy:
///   - Append incoming token to buffer.
///   - Whenever buffer length exceeds 2 × maxPatternLength, flush the
///     "safe prefix" — the part beyond the lookahead window — through the filter.
///   - Final Flush() emits any remainder.
/// </summary>
public sealed class StreamingOutputFilter
{
    /// <summary>
    /// Max plausible PII pattern length (e.g. 19-digit bank account, longer system prompt phrase).
    /// Buffer keeps at least this many trailing chars to avoid emitting a partial match.
    /// </summary>
    private const int LookaheadChars = 256;

    private readonly IChatOutputFilter _filter;
    private readonly System.Text.StringBuilder _buffer = new();
    private int _totalRedactionCount;
    private readonly HashSet<string> _redactionTypes = new(StringComparer.Ordinal);

    public StreamingOutputFilter(IChatOutputFilter filter)
    {
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
    }

    public int TotalRedactionCount => _totalRedactionCount;
    public IReadOnlyCollection<string> RedactionTypes => _redactionTypes;

    /// <summary>
    /// Feed an incoming token. Returns the safely-redacted text that may now be emitted
    /// to the client (may be empty if everything is still in the lookahead window).
    /// </summary>
    public string Append(string token)
    {
        if (string.IsNullOrEmpty(token))
            return string.Empty;

        _buffer.Append(token);

        if (_buffer.Length <= LookaheadChars)
            return string.Empty;

        var emitLength = _buffer.Length - LookaheadChars;
        var prefix = _buffer.ToString(0, emitLength);
        _buffer.Remove(0, emitLength);

        return ApplyFilter(prefix);
    }

    /// <summary>
    /// Flush any remaining buffered tokens through the filter and return the redacted text.
    /// Call exactly once at end of stream.
    /// </summary>
    public string Flush()
    {
        if (_buffer.Length == 0)
            return string.Empty;

        var remainder = _buffer.ToString();
        _buffer.Clear();
        return ApplyFilter(remainder);
    }

    private string ApplyFilter(string text)
    {
        var result = _filter.Sanitize(text);
        if (result.RedactionCount > 0)
        {
            _totalRedactionCount += result.RedactionCount;
            foreach (var type in result.RedactionTypes)
                _redactionTypes.Add(type);
        }
        return result.SanitizedResponse;
    }
}
