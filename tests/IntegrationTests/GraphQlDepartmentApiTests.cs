using System.Text.Json;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlDepartmentApiTests
{
    [Fact]
    public async Task GetDepartments_LegacyFieldName_RemainsAvailable()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Department Workspace", "department-workspace").Value;
        var department = Department.Create("Finance", tenant.Id).Value;
        var account = Account.Create("department.admin@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
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
            account.Email,
            RoleType.TenantAdmin,
            tenant.Id,
            membership.Id);

        const string query = """
            query {
              getDepartments {
                id
                name
                isActive
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query);

        var departments = json.RootElement.GetProperty("data").GetProperty("getDepartments");
        Assert.Equal(JsonValueKind.Array, departments.ValueKind);
        Assert.Single(departments.EnumerateArray());
    }
}
