using FinFlow.Domain.Interfaces;
using Microsoft.AspNetCore.Http;

namespace FinFlow.Infrastructure.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentTenantWriter currentTenantWriter)
    {
        var user = context.User;

        var roleClaim = user.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;
        var isSuperAdmin = roleClaim == "SuperAdmin";

        var tenantIdClaim = user.FindFirst("IdTenant")?.Value;
        var membershipIdClaim = user.FindFirst("MembershipId")?.Value;

        Guid? tenantId = Guid.TryParse(tenantIdClaim, out var t) ? t : null;
        Guid? membershipId = Guid.TryParse(membershipIdClaim, out var m) ? m : null;

        currentTenantWriter.SetFromRequest(tenantId, membershipId, isSuperAdmin);

        await _next(context);
    }
}
