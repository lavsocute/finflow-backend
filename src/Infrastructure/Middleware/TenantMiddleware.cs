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

    public async Task InvokeAsync(HttpContext context, ICurrentTenant currentTenant)
    {
        var user = context.User;

        // 1. Check if User is Super Admin (Role claim)
        var roleClaim = user.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;
        if (roleClaim == "SuperAdmin")
        {
            currentTenant.IsSuperAdmin = true;
        }

        // 2. Check for specific Tenant ID (IdTenant claim)
        var tenantIdClaim = user.FindFirst("IdTenant")?.Value;

        if (Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            currentTenant.Id = tenantId;
        }
        else
        {
            // Không có IdTenant
            if (!currentTenant.IsSuperAdmin)
            {
                // User thường mà không có Tenant -> Không có quyền truy cập dữ liệu tenant nào
                currentTenant.Id = null;
            }
            // Nếu là Super Admin và không có IdTenant -> IsSuperAdmin = true, Id = null -> Xem tất cả
        }

        await _next(context);
    }
}
