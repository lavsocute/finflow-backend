using Serilog;
using Serilog.AspNetCore;

namespace FinFlow.Api.Observability;

internal static class RequestLogEnricher
{
    public static void Enrich(IDiagnosticContext diagnosticContext, HttpContext httpContext)
    {
        diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
        diagnosticContext.Set("RequestPath", httpContext.Request.Path.Value);
        diagnosticContext.Set("RequestMethod", httpContext.Request.Method);

        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
            return;

        var accountId = user.FindFirst("sub")?.Value
                        ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        var membershipId = user.FindFirst("MembershipId")?.Value;
        var tenantId = user.FindFirst("IdTenant")?.Value;
        var role = user.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value
                   ?? user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (!string.IsNullOrWhiteSpace(accountId))
            diagnosticContext.Set("AccountId", accountId);
        if (!string.IsNullOrWhiteSpace(membershipId))
            diagnosticContext.Set("MembershipId", membershipId);
        if (!string.IsNullOrWhiteSpace(tenantId))
            diagnosticContext.Set("TenantId", tenantId);
        if (!string.IsNullOrWhiteSpace(role))
            diagnosticContext.Set("Role", role);
    }
}
