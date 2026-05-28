using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.IntegrationTests;

public sealed class TenantBrandingAssetUploadTests
{
    [Fact]
    public async Task TenantAdmin_CanUploadBrandingLogo_AndReceivePublicUrl()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Brand Upload Workspace", "brand-upload-workspace").Value;
        var account = Account.Create("brand.admin@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin, isOwner: true).Value;

        await factory.SeedAsync(db => db.AddRange(account, tenant, membership));

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.TenantAdmin,
            tenant.Id,
            membership.Id);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("logo"), "kind");
        var logoContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]);
        logoContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(logoContent, "file", "logo.png");

        using var response = await client.PostAsync("/api/tenant-settings/branding-assets", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<BrandingAssetUploadResponse>();
        Assert.NotNull(payload);
        Assert.StartsWith($"/uploads/tenant-branding/{tenant.Id:N}/logo-", payload!.Url);
        Assert.EndsWith(".png", payload.Url);

        using var assetResponse = await client.GetAsync(payload.Url);
        Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
        Assert.Equal("image/png", assetResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Staff_CannotUploadBrandingAsset()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Brand Staff Workspace", "brand-staff-workspace").Value;
        var account = Account.Create("brand.staff@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Staff).Value;

        await factory.SeedAsync(db => db.AddRange(account, tenant, membership));

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.Staff,
            tenant.Id,
            membership.Id);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("logo"), "kind");
        var logoContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        logoContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(logoContent, "file", "logo.png");

        using var response = await client.PostAsync("/api/tenant-settings/branding-assets", form);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record BrandingAssetUploadResponse(string Url);
}
