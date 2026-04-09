using FinFlow.Application.Auth.Dtos;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.IntegrationTests;

public sealed class TenantApprovalIntegrationTests
{
    private readonly AuthFlowTestFixture _fixture = new();

    [Fact]
    public async Task CreateIsolatedTenant_WithTenantAdminMember_SubmitsPendingRequest()
    {
        using var scope = _fixture.CreateScope();

        var currentTenant = scope.SeedTenant("Current Workspace", "current-iso");
        var currentDepartment = scope.SeedDepartment("Root", currentTenant.Id);
        var account = scope.SeedAccount("iso@finflow.test", "P@ssw0rd!", currentDepartment.Id);
        var currentMembership = scope.SeedMembership(account.Id, currentTenant.Id, RoleType.TenantAdmin);

        await scope.SaveSeedAsync();

        var result = await scope.AuthService.CreateIsolatedTenantAsync(
            new CreateIsolatedTenantRequest(
                account.Id,
                currentMembership.Id,
                "Enterprise Workspace",
                "enterprise-workspace",
                "VND",
                new CompanyInfoRequest("Enterprise Co", "1234567890", "HN", "0123456789", "Alice", "Manufacturing", 500)));

        Assert.True(result.IsSuccess, result.IsFailure ? $"{result.Error.Code}: {result.Error.Description}" : "Expected success.");
        Assert.Equal(TenantApprovalStatus.Pending, result.Value.Status);

        var request = await scope.DbContext.Set<TenantApprovalRequest>()
            .SingleAsync(x => x.TenantCode == "enterprise-workspace");

        Assert.Equal(account.Id, request.RequestedById);
        Assert.Equal(TenantApprovalStatus.Pending, request.Status);
        Assert.Equal(TenancyModel.Isolated, request.TenancyModel);
        Assert.Equal("Enterprise Co", request.CompanyName);
        Assert.Equal("1234567890", request.TaxCode);
    }

    [Fact]
    public async Task GetPendingTenantRequests_WithSuperAdmin_ReturnsSubmittedRequests()
    {
        using var scope = _fixture.CreateScope();

        scope.SeedTenantApprovalRequest(
            "pending-workspace",
            "Pending Workspace",
            "Pending Co",
            "1234567890",
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(7));

        await scope.SaveSeedAsync();
        scope.ActAsSuperAdmin();

        var result = await scope.AuthService.GetPendingTenantRequestsAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("pending-workspace", result.Value[0].TenantCode);
        Assert.Equal(TenantApprovalStatus.Pending, result.Value[0].Status);
    }

    [Fact]
    public async Task ApproveTenant_WithSuperAdmin_CreatesTenant_AndOwnerMembership()
    {
        using var scope = _fixture.CreateScope();

        var sourceTenant = scope.SeedTenant("Source Workspace", "source-approve");
        var sourceDepartment = scope.SeedDepartment("Root", sourceTenant.Id);
        var requester = scope.SeedAccount("approve@finflow.test", "P@ssw0rd!", sourceDepartment.Id);

        var request = scope.SeedTenantApprovalRequest(
            "approved-enterprise",
            "Approved Enterprise",
            "Approved Co",
            "1234567890",
            requester.Id,
            DateTime.UtcNow.AddDays(7));

        await scope.SaveSeedAsync();
        scope.ActAsSuperAdmin();

        var result = await scope.AuthService.ApproveTenantAsync(request.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? $"{result.Error.Code}: {result.Error.Description}" : "Expected success.");
        Assert.Equal(TenantApprovalStatus.Approved, result.Value.Status);
        Assert.NotNull(result.Value.TenantId);

        var tenant = await scope.DbContext.Set<Tenant>()
            .SingleAsync(x => x.TenantCode == "approved-enterprise");
        var membership = await scope.DbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == requester.Id && x.IdTenant == tenant.Id);
        var updatedRequest = await scope.DbContext.Set<TenantApprovalRequest>()
            .SingleAsync(x => x.Id == request.Id);

        Assert.Equal(TenancyModel.Isolated, tenant.TenancyModel);
        Assert.Equal("Approved Co", tenant.CompanyName);
        Assert.Equal("1234567890", tenant.TaxCode);
        Assert.True(membership.IsOwner);
        Assert.Equal(RoleType.TenantAdmin, membership.Role);
        Assert.Equal(TenantApprovalStatus.Approved, updatedRequest.Status);
    }

    [Fact]
    public async Task RejectTenant_WithSuperAdmin_UpdatesStatusAndReason()
    {
        using var scope = _fixture.CreateScope();

        var request = scope.SeedTenantApprovalRequest(
            "rejected-enterprise",
            "Rejected Enterprise",
            "Rejected Co",
            "1234567890",
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(7));

        await scope.SaveSeedAsync();
        scope.ActAsSuperAdmin();

        var result = await scope.AuthService.RejectTenantAsync(request.Id, "Missing enterprise verification");

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantApprovalStatus.Rejected, result.Value.Status);

        var updatedRequest = await scope.DbContext.Set<TenantApprovalRequest>()
            .SingleAsync(x => x.Id == request.Id);

        Assert.Equal(TenantApprovalStatus.Rejected, updatedRequest.Status);
        Assert.Equal("Missing enterprise verification", updatedRequest.RejectionReason);
    }

    [Fact]
    public async Task ApproveTenant_Fails_WhenCallerIsNotSuperAdmin()
    {
        using var scope = _fixture.CreateScope();

        var request = scope.SeedTenantApprovalRequest(
            "unauthorized-enterprise",
            "Unauthorized Enterprise",
            "Unauthorized Co",
            "1234567890",
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(7));

        await scope.SaveSeedAsync();

        var result = await scope.AuthService.ApproveTenantAsync(request.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(TenantApprovalRequestErrors.Unauthorized.Code, result.Error.Code);
    }

    [Fact]
    public async Task ApproveTenant_Fails_WhenTenantCodeIsBlockedByRecentRejection()
    {
        using var scope = _fixture.CreateScope();

        var sourceTenant = scope.SeedTenant("Source Workspace", "source-blocked-approve");
        var sourceDepartment = scope.SeedDepartment("Root", sourceTenant.Id);
        var requester = scope.SeedAccount("approve-blocked@finflow.test", "P@ssw0rd!", sourceDepartment.Id);

        var rejectedRequest = scope.SeedTenantApprovalRequest(
            "cooldown-enterprise",
            "Cooldown Enterprise",
            "Cooldown Co",
            "1234567890",
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(7));
        Assert.True(rejectedRequest.Reject("Rejected recently").IsSuccess);

        var pendingRequest = scope.SeedTenantApprovalRequest(
            "cooldown-enterprise",
            "Pending Cooldown Enterprise",
            "Pending Cooldown Co",
            "1234567891",
            requester.Id,
            DateTime.UtcNow.AddDays(7));

        await scope.SaveSeedAsync();
        scope.ActAsSuperAdmin();

        var result = await scope.AuthService.ApproveTenantAsync(pendingRequest.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(TenantErrors.CodeBlocked.Code, result.Error.Code);
    }

    [Fact]
    public async Task CreateIsolatedTenant_Fails_WhenTenantCodeIsBlockedByRecentRejection()
    {
        using var scope = _fixture.CreateScope();

        var currentTenant = scope.SeedTenant("Current Workspace", "current-block");
        var currentDepartment = scope.SeedDepartment("Root", currentTenant.Id);
        var account = scope.SeedAccount("blocked@finflow.test", "P@ssw0rd!", currentDepartment.Id);
        var currentMembership = scope.SeedMembership(account.Id, currentTenant.Id, RoleType.TenantAdmin);

        var oldRequester = scope.SeedAccount("old@finflow.test", "P@ssw0rd!", currentDepartment.Id);
        var rejectedRequest = scope.SeedTenantApprovalRequest(
            "blocked-code",
            "Blocked Enterprise",
            "Blocked Co",
            "1234567890",
            oldRequester.Id,
            DateTime.UtcNow.AddDays(7));
        Assert.True(rejectedRequest.Reject("Rejected").IsSuccess);

        await scope.SaveSeedAsync();

        var result = await scope.AuthService.CreateIsolatedTenantAsync(
            new CreateIsolatedTenantRequest(
                account.Id,
                currentMembership.Id,
                "Retry Enterprise",
                "blocked-code",
                "VND",
                new CompanyInfoRequest("Retry Co", "1234567891")));

        Assert.True(result.IsFailure);
        Assert.Equal(TenantErrors.CodeBlocked.Code, result.Error.Code);
    }

    [Fact]
    public async Task CreateIsolatedTenant_Fails_WhenRequesterAccountIsInactive()
    {
        using var scope = _fixture.CreateScope();

        var currentTenant = scope.SeedTenant("Current Workspace", "current-inactive-requester");
        var currentDepartment = scope.SeedDepartment("Root", currentTenant.Id);
        var account = scope.SeedAccount("inactive-requester@finflow.test", "P@ssw0rd!", currentDepartment.Id);
        var currentMembership = scope.SeedMembership(account.Id, currentTenant.Id, RoleType.TenantAdmin);

        Assert.True(account.Deactivate().IsSuccess);
        await scope.SaveSeedAsync();

        var result = await scope.AuthService.CreateIsolatedTenantAsync(
            new CreateIsolatedTenantRequest(
                account.Id,
                currentMembership.Id,
                "Inactive Requester Workspace",
                "inactive-requester-workspace",
                "VND",
                new CompanyInfoRequest("Inactive Requester Co", "1234567890")));

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.Unauthorized.Code, result.Error.Code);
    }
}
