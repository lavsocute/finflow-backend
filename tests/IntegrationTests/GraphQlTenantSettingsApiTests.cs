using System.Text.Json;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlTenantSettingsApiTests
{
    [Fact]
    public async Task GetTenantSettings_LegacyFieldName_RemainsAvailable()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Settings Workspace", "settings-workspace").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var account = Account.Create("settings.admin@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin, isOwner: true).Value;
        var settings = TenantSettings.CreateDefault(tenant.Id);
        settings.UpdateBranding(
            logoUrl: null,
            faviconUrl: null,
            primaryColor: "#12AB34",
            companyDisplayName: "FinFlow Settings Workspace",
            locale: "vi-VN",
            timezone: "Asia/Ho_Chi_Minh");

        await factory.SeedAsync(db =>
        {
            db.Add(tenant);
            db.Add(department);
            db.Add(account);
            db.Add(membership);
            db.Add(settings);
        });

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.TenantAdmin,
            tenant.Id,
            membership.Id);

        const string query = """
            query {
              getTenantSettings {
                id
                branding {
                  primaryColor
                  companyDisplayName
                }
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query);

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("getTenantSettings");
        Assert.Equal(settings.Id.ToString(), payload.GetProperty("id").GetString());

        var branding = payload.GetProperty("branding");
        Assert.Equal("#12AB34", branding.GetProperty("primaryColor").GetString());
        Assert.Equal("FinFlow Settings Workspace", branding.GetProperty("companyDisplayName").GetString());
    }

    [Fact]
    public async Task GetTenantSettings_CreatesDefaultSettings_WhenTenantHasNoSettingsRow()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Missing Settings Workspace", "missing-settings-workspace").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var account = Account.Create("settings.manager@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Manager).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(tenant);
            db.Add(department);
            db.Add(account);
            db.Add(membership);
        });

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.Manager,
            tenant.Id,
            membership.Id);

        const string query = """
            query {
              getTenantSettings {
                branding {
                  locale
                  timezone
                }
                budgetPolicy {
                  defaultEnforcementMode
                }
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query);

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("getTenantSettings");
        var branding = payload.GetProperty("branding");
        Assert.Equal("vi-VN", branding.GetProperty("locale").GetString());
        Assert.Equal("Asia/Ho_Chi_Minh", branding.GetProperty("timezone").GetString());
        Assert.Equal("SoftBlock", payload.GetProperty("budgetPolicy").GetProperty("defaultEnforcementMode").GetString());

        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull(await db.Set<TenantSettings>().FirstOrDefaultAsync(x => x.IdTenant == tenant.Id));
    }
}
