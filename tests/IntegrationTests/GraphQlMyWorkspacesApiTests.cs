using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlMyWorkspacesApiTests
{
    [Fact]
    public async Task MyWorkspaces_Query_Returns_ServerBackedMemberships_ForAccountSession()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantOne = Tenant.Create("Workspace One", "workspace-one").Value;
        var tenantTwo = Tenant.Create("Workspace Two", "workspace-two").Value;
        var departmentOne = Department.Create("Root", tenantOne.Id).Value;
        var departmentTwo = Department.Create("Root", tenantTwo.Id).Value;
        var account = Account.Create("workspaces.user@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membershipOne = TenantMembership.Create(account.Id, tenantOne.Id, RoleType.TenantAdmin, isOwner: true).Value;
        var membershipTwo = TenantMembership.Create(account.Id, tenantTwo.Id, RoleType.Accountant).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(tenantOne, tenantTwo, departmentOne, departmentTwo, account, membershipOne, membershipTwo);
        });

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Accountant);
        const string query = """
            query {
              myWorkspaces {
                workspaceId
                tenantId
                tenantCode
                tenantName
                membershipId
                role
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query);

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var items = json.RootElement.GetProperty("data").GetProperty("myWorkspaces").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item.GetProperty("membershipId").GetGuid() == membershipOne.Id);
        Assert.Contains(items, item => item.GetProperty("membershipId").GetGuid() == membershipTwo.Id);
    }

    [Fact]
    public async Task SelectWorkspace_Mutation_Returns_WorkspaceSession_ForOwnedMembership()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Selectable Workspace", "selectable-workspace").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var account = Account.Create("select.user@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin, isOwner: true).Value;

        await factory.SeedAsync(db =>
        {
            db.AddRange(tenant, department, account, membership);
        });

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Accountant);
        const string mutation = """
            mutation($input: SelectWorkspaceInput!) {
              selectWorkspace(input: $input) {
                accessToken
                refreshToken
                accountId
                membershipId
                email
                role
                tenantId
                sessionKind
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, new
        {
            input = new { membershipId = membership.Id }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("selectWorkspace");
        Assert.Equal(account.Id, payload.GetProperty("accountId").GetGuid());
        Assert.Equal(membership.Id, payload.GetProperty("membershipId").GetGuid());
        Assert.Equal(tenant.Id, payload.GetProperty("tenantId").GetGuid());
        Assert.Equal("workspace", payload.GetProperty("sessionKind").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task CreateWorkspace_Mutation_Creates_FirstWorkspace_ForAccountSession()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("creator.user@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        await factory.SeedAsync(db => db.Add(account));

        using var client = factory.CreateAuthenticatedClient(account.Id, account.Email, RoleType.Accountant);
        const string mutation = """
            mutation($input: CreateSharedTenantInput!) {
              createWorkspace(input: $input) {
                accessToken
                refreshToken
                accountId
                membershipId
                tenantId
                email
                role
                sessionKind
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, new
        {
            input = new { name = "Created Workspace", tenantCode = "created-workspace", currency = "VND" }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("createWorkspace");
        Assert.Equal(account.Id, payload.GetProperty("accountId").GetGuid());
        Assert.Equal("creator.user@finflow.test", payload.GetProperty("email").GetString());
        Assert.Equal("workspace", payload.GetProperty("sessionKind").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await dbContext.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.TenantCode == "created-workspace");
        var membership = await dbContext.Set<TenantMembership>().IgnoreQueryFilters().SingleAsync(x => x.AccountId == account.Id && x.IdTenant == tenant.Id);
        var department = await dbContext.Set<Department>().IgnoreQueryFilters().SingleAsync(x => x.IdTenant == tenant.Id);

        Assert.Equal(tenant.Id, payload.GetProperty("tenantId").GetGuid());
        Assert.Equal(membership.Id, payload.GetProperty("membershipId").GetGuid());
        Assert.Equal("Root", department.Name);
        Assert.True(membership.IsOwner);
    }
}
