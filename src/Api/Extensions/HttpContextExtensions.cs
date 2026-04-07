using Microsoft.AspNetCore.Http;

namespace FinFlow.Api.Extensions;

public static class HttpContextExtensions
{
    public static string GetClientIpAddress(this HttpContext context)
    {
        // Trust RemoteIpAddress because UseForwardedHeaders middleware has already processed headers securely.
        // Reading headers manually here would allow IP spoofing.
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
