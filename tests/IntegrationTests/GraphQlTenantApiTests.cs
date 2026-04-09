using System.Text.Json;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlTenantApiTests
{
    [Fact]
    public async Task CreateSharedTenant_Mutation_Works_ThroughHttpPipeline()
    {
        await using var factory = new GraphQlApiTestFactory();

        var currentTenant = Tenant.Create("Current Workspace", "http-shared-current").Value;
        var currentDepartment = Department.Create("Root", currentTenant.Id).Value;
        var account = Account.Create("http.shared@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!"), currentDepartment.Id).Value;
        var membership = TenantMembership.Create(account.Id, currentTenant.Id, RoleType.TenantAdmin).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(currentTenant);
            db.Add(currentDepartment);
            db.Add(account);
            db.Add(membership);
        });

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.TenantAdmin,
            currentTenant.Id,
            membership.Id);

        const string query = """
            mutation($input: CreateSharedTenantInput!) {
              createSharedTenant(input: $input) {
                id
                membershipId
                idTenant
                role
                email
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query, new
        {
            input = new { name = "HTTP Shared Workspace", tenantCode = "http-shared-new", currency = "VND" }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("createSharedTenant");
        Assert.Equal(account.Email, payload.GetProperty("email").GetString());
        Assert.Equal("TENANT_ADMIN", payload.GetProperty("role").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createdTenant = await dbContext.Set<Tenant>().SingleAsync(x => x.TenantCode == "http-shared-new");
        var ownerMembership = await dbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == account.Id && x.IdTenant == createdTenant.Id);

        Assert.True(ownerMembership.IsOwner);
    }

    [Fact]
    public async Task CreateIsolatedTenant_Mutation_Works_ThroughHttpPipeline()
    {
        await using var factory = new GraphQlApiTestFactory();

        var currentTenant = Tenant.Create("Current Workspace", "http-iso-current").Value;
        var currentDepartment = Department.Create("Root", currentTenant.Id).Value;
        var account = Account.Create("http.iso@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!"), currentDepartment.Id).Value;
        var membership = TenantMembership.Create(account.Id, currentTenant.Id, RoleType.TenantAdmin).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(currentTenant);
            db.Add(currentDepartment);
            db.Add(account);
            db.Add(membership);
        });

        using (var tenantAdminClient = factory.CreateAuthenticatedClient(
                   account.Id,
                   account.Email,
                   RoleType.TenantAdmin,
                   currentTenant.Id,
                   membership.Id))
        {
            const string mutation = """
                mutation($input: CreateIsolatedTenantInput!) {
                  createIsolatedTenant(input: $input) {
                    requestId
                    status
                    message
                  }
                }
                """;

            using var mutationJson = await GraphQlApiTestFactory.PostGraphQlAsync(tenantAdminClient, mutation, new
            {
                input = new
                {
                    name = "HTTP Isolated Workspace",
                    tenantCode = "http-isolated-new",
                    currency = "VND",
                    companyInfo = new
                    {
                        companyName = "HTTP Enterprise Co",
                        taxCode = "1234567890",
                        address = "HCM",
                        phone = "0123456789",
                        contactPerson = "Alice",
                        businessType = "Tech",
                        employeeCount = 120
                    }
                }
            });

            Assert.False(mutationJson.RootElement.TryGetProperty("errors", out _), mutationJson.RootElement.ToString());
            Assert.Equal("Pending", mutationJson.RootElement.GetProperty("data").GetProperty("createIsolatedTenant").GetProperty("status").GetString());
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull(await dbContext.Set<TenantApprovalRequest>().SingleAsync(x => x.TenantCode == "http-isolated-new"));
    }

    [Fact]
    public async Task CreateIsolatedTenant_Mutation_ReturnsValidationErrors_ForMissingCompanyInfoFields()
    {
        await using var factory = new GraphQlApiTestFactory();

        var currentTenant = Tenant.Create("Current Workspace", "http-iso-validation-current").Value;
        var currentDepartment = Department.Create("Root", currentTenant.Id).Value;
        var account = Account.Create("http.iso.validation@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!"), currentDepartment.Id).Value;
        var membership = TenantMembership.Create(account.Id, currentTenant.Id, RoleType.TenantAdmin).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(currentTenant);
            db.Add(currentDepartment);
            db.Add(account);
            db.Add(membership);
        });

        using var tenantAdminClient = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.TenantAdmin,
            currentTenant.Id,
            membership.Id);

        const string mutation = """
            mutation($input: CreateIsolatedTenantInput!) {
              createIsolatedTenant(input: $input) {
                requestId
                status
                message
              }
            }
            """;

        using var missingCompanyInfoJson = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(tenantAdminClient, mutation, new
        {
            input = new
            {
                name = "HTTP Invalid Isolated Workspace",
                tenantCode = "http-invalid-iso-company",
                currency = "VND",
                companyInfo = (object?)null
            }
        });

        var missingCompanyInfoErrors = missingCompanyInfoJson.RootElement.GetProperty("errors");
        Assert.Equal("Tenant.CompanyInfoRequired", missingCompanyInfoErrors[0].GetProperty("extensions").GetProperty("code").GetString());

        using var blankCompanyNameJson = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(tenantAdminClient, mutation, new
        {
            input = new
            {
                name = "HTTP Invalid Isolated Workspace",
                tenantCode = "http-invalid-iso-name",
                currency = "VND",
                companyInfo = new
                {
                    companyName = "   ",
                    taxCode = "1234567890"
                }
            }
        });

        var blankCompanyNameErrors = blankCompanyNameJson.RootElement.GetProperty("errors");
        Assert.Equal("Tenant.CompanyNameRequired", blankCompanyNameErrors[0].GetProperty("extensions").GetProperty("code").GetString());

        using var blankTaxCodeJson = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(tenantAdminClient, mutation, new
        {
            input = new
            {
                name = "HTTP Invalid Isolated Workspace",
                tenantCode = "http-invalid-iso-tax",
                currency = "VND",
                companyInfo = new
                {
                    companyName = "FinFlow Company",
                    taxCode = "   "
                }
            }
        });

        var blankTaxCodeErrors = blankTaxCodeJson.RootElement.GetProperty("errors");
        Assert.Equal("Tenant.TaxCodeRequired", blankTaxCodeErrors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PendingTenantRequests_Query_Works_ThroughHttpPipeline()
    {
        await using var factory = new GraphQlApiTestFactory();

        var requesterTenant = Tenant.Create("Requester Workspace", "http-pending-source").Value;
        var requesterDepartment = Department.Create("Root", requesterTenant.Id).Value;
        var requester = Account.Create("pending@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!"), requesterDepartment.Id).Value;
        var request = TenantApprovalRequest.Create(
            "http-pending-target",
            "Pending Target",
            "Pending Co",
            "1234567890",
            null,
            null,
            null,
            null,
            null,
            "VND",
            requester.Id,
            DateTime.UtcNow.AddDays(7)).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(requesterTenant);
            db.Add(requesterDepartment);
            db.Add(requester);
            db.Add(request);
        });

        using var superAdminClient = factory.CreateAuthenticatedClient(
            Guid.NewGuid(),
            "superadmin@finflow.test",
            RoleType.SuperAdmin);

        const string pendingQuery = """
            query {
              pendingTenantRequests {
                tenantCode
                status
              }
            }
            """;

        using var queryJson = await GraphQlApiTestFactory.PostGraphQlAsync(superAdminClient, pendingQuery);
        Assert.False(queryJson.RootElement.TryGetProperty("errors", out _), queryJson.RootElement.ToString());

        var requests = queryJson.RootElement.GetProperty("data").GetProperty("pendingTenantRequests");
        Assert.Contains(requests.EnumerateArray(), x => x.GetProperty("tenantCode").GetString() == "http-pending-target");
    }

    [Fact]
    public async Task ApproveTenant_And_RejectTenant_Mutations_Work_ThroughHttpPipeline()
    {
        await using var factory = new GraphQlApiTestFactory();

        var sourceTenant = Tenant.Create("Source Workspace", "http-approval-source").Value;
        var sourceDepartment = Department.Create("Root", sourceTenant.Id).Value;
        var requester = Account.Create("requester@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!"), sourceDepartment.Id).Value;
        var approveRequest = TenantApprovalRequest.Create(
            "http-approve-target",
            "Approve Target",
            "Approve Co",
            "1234567890",
            null,
            null,
            null,
            null,
            null,
            "VND",
            requester.Id,
            DateTime.UtcNow.AddDays(7)).Value;
        var rejectRequest = TenantApprovalRequest.Create(
            "http-reject-target",
            "Reject Target",
            "Reject Co",
            "1234567891",
            null,
            null,
            null,
            null,
            null,
            "VND",
            requester.Id,
            DateTime.UtcNow.AddDays(7)).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(sourceTenant);
            db.Add(sourceDepartment);
            db.Add(requester);
            db.Add(approveRequest);
            db.Add(rejectRequest);
        });

        using var superAdminClient = factory.CreateAuthenticatedClient(
            Guid.NewGuid(),
            "superadmin@finflow.test",
            RoleType.SuperAdmin);

        const string approveMutation = """
            mutation($requestId: UUID!) {
              approveTenant(requestId: $requestId) {
                requestId
                status
                tenantCode
              }
            }
            """;

        using var approveJson = await GraphQlApiTestFactory.PostGraphQlAsync(superAdminClient, approveMutation, new { requestId = approveRequest.Id });
        Assert.False(approveJson.RootElement.TryGetProperty("errors", out _), approveJson.RootElement.ToString());
        Assert.Equal("Approved", approveJson.RootElement.GetProperty("data").GetProperty("approveTenant").GetProperty("status").GetString());

        const string rejectMutation = """
            mutation($requestId: UUID!, $reason: String!) {
              rejectTenant(requestId: $requestId, reason: $reason) {
                requestId
                status
                name
              }
            }
            """;

        using var rejectJson = await GraphQlApiTestFactory.PostGraphQlAsync(superAdminClient, rejectMutation, new
        {
            requestId = rejectRequest.Id,
            reason = "Missing documents"
        });

        Assert.False(rejectJson.RootElement.TryGetProperty("errors", out _), rejectJson.RootElement.ToString());
        Assert.Equal("Rejected", rejectJson.RootElement.GetProperty("data").GetProperty("rejectTenant").GetProperty("status").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotNull(await dbContext.Set<Tenant>().SingleAsync(x => x.TenantCode == "http-approve-target"));
        Assert.Equal(TenantApprovalStatus.Rejected, (await dbContext.Set<TenantApprovalRequest>().SingleAsync(x => x.Id == rejectRequest.Id)).Status);
    }
}
