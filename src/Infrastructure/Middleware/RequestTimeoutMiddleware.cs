using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FinFlow.Infrastructure.Middleware;

public class RequestTimeoutMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTimeoutMiddleware> _logger;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);

    public RequestTimeoutMiddleware(RequestDelegate next, ILogger<RequestTimeoutMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        using var cts = new CancellationTokenSource(_defaultTimeout);

        var originalToken = context.RequestAborted;
        var compositeToken = CancellationTokenSource.CreateLinkedTokenSource(originalToken, cts.Token).Token;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestTimeout"] = _defaultTimeout.TotalSeconds
        }))
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                await _next(context);

                stopwatch.Stop();

                if (stopwatch.Elapsed > TimeSpan.FromSeconds(25))
                {
                    _logger.LogWarning(
                        "Request completed slowly: {Method} {Path} took {ElapsedMs}ms",
                        context.Request.Method,
                        context.Request.Path,
                        stopwatch.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !originalToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Request timed out: {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                await context.Response.WriteAsync("Request timeout");
            }
        }
    }
}

public static class RequestTimeoutMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestTimeout(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestTimeoutMiddleware>();
    }
}