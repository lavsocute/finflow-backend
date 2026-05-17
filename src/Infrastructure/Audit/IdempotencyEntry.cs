namespace FinFlow.Infrastructure.Audit;

/// <summary>
/// Cached representation of a successful idempotent request response.
/// Used by <see cref="IdempotencyMiddleware"/> to replay results when a
/// client retries a mutation with the same Idempotency-Key.
/// </summary>
public sealed class IdempotencyEntry
{
    public int StatusCode { get; set; }
    public string ContentType { get; set; } = "application/json";
    public string BodyBase64 { get; set; } = string.Empty;
    public string RequestBodyHash { get; set; } = string.Empty;
}
