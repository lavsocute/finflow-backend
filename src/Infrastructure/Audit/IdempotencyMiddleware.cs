using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FinFlow.Infrastructure.Audit;

/// <summary>
/// Idempotency-Key middleware. When a critical mutation is replayed with the
/// same Idempotency-Key the cached response is returned without re-executing
/// the operation. If the body hash mismatches, a GraphQL conflict error is
/// returned. If the cache backend is unavailable the middleware fails open
/// (logs a warning and lets the request proceed).
/// </summary>
public sealed class IdempotencyMiddleware
{
    private const string HeaderName = "Idempotency-Key";
    private const int MaxBodyBytes = 1_048_576; // 1 MB
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private static readonly Regex MutationNameRegex =
        new(@"mutation\s*(?:\w+\s*)?\{\s*(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> CriticalMutations = new(StringComparer.OrdinalIgnoreCase)
    {
        "submitDocument",
        "approveReviewedDocument",
        "rejectReviewedDocument",
        "recordPayment",
        "confirmPayment",
        "rejectPayment",
        "updatePayment",
        "cancelPayment",
        "refundPayment",
        "rejectExpense",
        "reopenExpense"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICacheService cache, ICurrentTenant currentTenant)
    {
        if (!ShouldHandle(context))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var keyValues) || keyValues.Count == 0)
        {
            await _next(context);
            return;
        }

        var rawKey = keyValues.ToString();
        if (!Guid.TryParse(rawKey, out var idempotencyKey))
        {
            await WriteGraphQlErrorAsync(
                context,
                "Idempotency-Key header must be a UUID.",
                "Idempotency.InvalidKey");
            return;
        }

        context.Request.EnableBuffering();
        var bodyContent = await ReadBodyAsync(context);
        if (bodyContent is null)
        {
            await _next(context);
            return;
        }

        var mutationName = ExtractMutationName(bodyContent);
        if (mutationName is null || !CriticalMutations.Contains(mutationName))
        {
            await _next(context);
            return;
        }

        var bodyHash = ComputeSha256(bodyContent);
        var tenantId = currentTenant.Id?.ToString("N") ?? "anon";
        var cacheKey = $"idempotency:{tenantId}:{idempotencyKey:N}";

        IdempotencyEntry? cached = null;
        try
        {
            cached = await cache.GetAsync<IdempotencyEntry>(cacheKey, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency cache lookup failed; allowing request through.");
        }

        if (cached is not null)
        {
            if (!string.Equals(cached.RequestBodyHash, bodyHash, StringComparison.Ordinal))
            {
                await WriteGraphQlErrorAsync(
                    context,
                    "Idempotency-Key was reused with a different request body.",
                    "Idempotency.KeyReuseConflict");
                return;
            }

            await ReplayCachedAsync(context, cached);
            return;
        }

        var originalBody = context.Response.Body;
        await using var captureStream = new MemoryStream();
        context.Response.Body = captureStream;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        captureStream.Position = 0;
        var responseBytes = captureStream.ToArray();

        // Only cache successful responses (2xx). Errors might be transient and should not be replayed.
        if (context.Response.StatusCode is >= 200 and < 300)
        {
            var entry = new IdempotencyEntry
            {
                StatusCode = context.Response.StatusCode,
                ContentType = context.Response.ContentType ?? "application/json",
                BodyBase64 = Convert.ToBase64String(responseBytes),
                RequestBodyHash = bodyHash
            };

            try
            {
                await cache.SetAsync(cacheKey, entry, CacheTtl, context.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Idempotency cache set failed for key {Key}.", cacheKey);
            }
        }

        if (responseBytes.Length > 0)
            await originalBody.WriteAsync(responseBytes, context.RequestAborted);
    }

    private static bool ShouldHandle(HttpContext context)
    {
        if (context.Request.Method != HttpMethods.Post)
            return false;

        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
            return false;

        return path.Contains("/graphql", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ReadBodyAsync(HttpContext context)
    {
        try
        {
            using var reader = new StreamReader(
                context.Request.Body,
                encoding: Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            var buffer = new char[MaxBodyBytes];
            var charsRead = await reader.ReadBlockAsync(buffer, 0, MaxBodyBytes);
            context.Request.Body.Position = 0;

            if (charsRead == 0)
                return null;

            return new string(buffer, 0, charsRead);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractMutationName(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("query", out var queryElement))
                return null;

            var query = queryElement.GetString();
            if (string.IsNullOrEmpty(query))
                return null;

            var match = MutationNameRegex.Match(query);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ComputeSha256(string content)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static async Task ReplayCachedAsync(HttpContext context, IdempotencyEntry entry)
    {
        context.Response.StatusCode = entry.StatusCode;
        context.Response.ContentType = entry.ContentType;

        var bytes = Convert.FromBase64String(entry.BodyBase64);
        if (bytes.Length > 0)
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
    }

    private static async Task WriteGraphQlErrorAsync(HttpContext context, string message, string code)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";

        var payload = JsonSerializer.Serialize(new
        {
            errors = new[]
            {
                new
                {
                    message,
                    extensions = new { code }
                }
            }
        });

        await context.Response.WriteAsync(payload, context.RequestAborted);
    }
}
