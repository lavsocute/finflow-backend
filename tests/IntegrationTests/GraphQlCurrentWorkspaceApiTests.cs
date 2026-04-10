using System.Text.Json;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlCurrentWorkspaceApiTests
{
    [Fact]
    public async Task CurrentWorkspace_Query_RequiresAuthentication()
    {
        await using var factory = new GraphQlApiTestFactory();
        using var client = factory.CreateClient();

        const string query = """
            query {
              currentWorkspace {
                accountId
                email
                membershipId
                role
                tenantId
                tenantCode
                tenantName
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, query);

        Assert.True(json.RootElement.TryGetProperty("errors", out var errors), json.RootElement.ToString());
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        Assert.Single(errors.EnumerateArray());
        Assert.Contains("not authorized to access this resource", errors[0].GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);

        if (json.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("currentWorkspace", out var currentWorkspace))
        {
            Assert.Equal(JsonValueKind.Null, currentWorkspace.ValueKind);
        }
    }

    [Fact]
    public async Task CurrentWorkspace_Query_ReturnsCanonicalWorkspaceData_ForAuthenticatedUser()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Canonical Workspace", "canonical-workspace").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var account = Account.Create("canonical.user@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin, isOwner: true).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(tenant);
            db.Add(department);
            db.Add(account);
            db.Add(membership);
        });

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            "jwt.claims.only@finflow.test",
            RoleType.Accountant,
            tenant.Id,
            membership.Id);

        const string query = """
            query {
              currentWorkspace {
                accountId
                email
                membershipId
                role
                tenantId
                tenantCode
                tenantName
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query);

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("currentWorkspace");
        Assert.Equal(account.Id.ToString(), payload.GetProperty("accountId").GetString());
        Assert.Equal(account.Email, payload.GetProperty("email").GetString());
        Assert.Equal(membership.Id.ToString(), payload.GetProperty("membershipId").GetString());
        Assert.Equal("TENANT_ADMIN", payload.GetProperty("role").GetString());
        Assert.Equal(tenant.Id.ToString(), payload.GetProperty("tenantId").GetString());
        Assert.Equal(tenant.TenantCode, payload.GetProperty("tenantCode").GetString());
        Assert.Equal(tenant.Name, payload.GetProperty("tenantName").GetString());
    }

    [Fact]
    public async Task CurrentWorkspace_Query_DerivesMembership_WhenAuthenticatedContextHasTenantWithoutMembershipId()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Derived Workspace", "derived-workspace").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var account = Account.Create("derived.user@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.Accountant).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(tenant);
            db.Add(department);
            db.Add(account);
            db.Add(membership);
        });

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            "claims.derived@finflow.test",
            RoleType.TenantAdmin,
            tenant.Id);

        const string query = """
            query {
              currentWorkspace {
                accountId
                email
                membershipId
                role
                tenantId
                tenantCode
                tenantName
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query);

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("currentWorkspace");
        Assert.Equal(account.Id.ToString(), payload.GetProperty("accountId").GetString());
        Assert.Equal(account.Email, payload.GetProperty("email").GetString());
        Assert.Equal(membership.Id.ToString(), payload.GetProperty("membershipId").GetString());
        Assert.Equal("ACCOUNTANT", payload.GetProperty("role").GetString());
        Assert.Equal(tenant.Id.ToString(), payload.GetProperty("tenantId").GetString());
        Assert.Equal(tenant.TenantCode, payload.GetProperty("tenantCode").GetString());
        Assert.Equal(tenant.Name, payload.GetProperty("tenantName").GetString());
    }

    [Fact]
    public async Task CurrentWorkspace_Query_ReturnsTenantNotFound_WhenWorkspaceTenantIsInactive()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Inactive Workspace", "inactive-workspace").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var account = Account.Create("inactive.user@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin).Value;
        tenant.Deactivate();

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
            RoleType.TenantAdmin,
            tenant.Id,
            membership.Id);

        const string query = """
            query {
              currentWorkspace {
                accountId
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, query);

        var errors = json.RootElement.GetProperty("errors");
        Assert.Equal("Tenant.NotFound", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }
}
