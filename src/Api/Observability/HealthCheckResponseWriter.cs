using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinFlow.Api.Observability;

internal static class HealthCheckResponseWriter
{
    public static Task Write(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                duration = entry.Value.Duration.TotalMilliseconds,
                description = entry.Value.Description
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
