using System.Security.Claims;
using FinFlow.Domain.Enums;

namespace FinFlow.Api.Endpoints;

public static class TenantBrandingAssetEndpoints
{
    private const long MaxAssetBytes = 1 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> AllowedContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/x-icon"] = ".ico",
            ["image/vnd.microsoft.icon"] = ".ico",
            ["image/webp"] = ".webp"
        };

    public static IEndpointRouteBuilder MapTenantBrandingAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost("/api/tenant-settings/branding-assets", UploadBrandingAssetAsync)
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> UploadBrandingAssetAsync(
        HttpContext httpContext,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var user = httpContext.User;
        if (!TryGetTenantId(user, out var tenantId))
            return Results.Unauthorized();

        if (!IsTenantAdmin(user))
            return Results.Forbid();

        var request = httpContext.Request;
        if (!request.HasFormContentType)
            return Results.BadRequest(new BrandingAssetUploadError("Expected a multipart form upload."));

        var form = await request.ReadFormAsync(cancellationToken);
        var kind = form["kind"].ToString().Trim().ToLowerInvariant();
        if (kind is not ("logo" or "favicon"))
            return Results.BadRequest(new BrandingAssetUploadError("Branding asset kind must be logo or favicon."));

        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return Results.BadRequest(new BrandingAssetUploadError("A branding image file is required."));

        if (file.Length > MaxAssetBytes)
            return Results.BadRequest(new BrandingAssetUploadError("Branding image must be 1 MB or smaller."));

        if (!AllowedContentTypes.TryGetValue(file.ContentType, out var extension))
            return Results.BadRequest(new BrandingAssetUploadError("Only PNG, JPG, ICO, and WEBP images are supported."));

        var webRootPath = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRootPath))
            webRootPath = System.IO.Path.Combine(environment.ContentRootPath, "wwwroot");

        var tenantFolder = System.IO.Path.Combine(webRootPath, "uploads", "tenant-branding", tenantId.ToString("N"));
        Directory.CreateDirectory(tenantFolder);

        var fileName = $"{kind}-{Guid.NewGuid():N}{extension}";
        var filePath = System.IO.Path.Combine(tenantFolder, fileName);

        await using (var stream = File.Create(filePath))
        await using (var uploadStream = file.OpenReadStream())
        {
            await uploadStream.CopyToAsync(stream, cancellationToken);
        }

        var url = $"/uploads/tenant-branding/{tenantId:N}/{fileName}";
        return Results.Ok(new BrandingAssetUploadResponse(url));
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var rawTenantId = user.FindFirst("IdTenant")?.Value;
        return Guid.TryParse(rawTenantId, out tenantId);
    }

    private static bool IsTenantAdmin(ClaimsPrincipal user)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value;
        return string.Equals(role, RoleType.TenantAdmin.ToString(), StringComparison.Ordinal);
    }

    private sealed record BrandingAssetUploadResponse(string Url);

    private sealed record BrandingAssetUploadError(string Message);
}
