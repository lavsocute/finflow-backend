using FinFlow.Application.Auth.Dtos;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.IntegrationTests;

public sealed class CreateTenantIntegrationTests
{
    private readonly AuthFlowTestFixture _fixture = new();

    [Fact]
    public async Task CreateSharedTenant_WithTenantAdminMember_CreatesTenantOwnerMembership_AndReturnsNewContext()
    {
        using var scope = _fixture.CreateScope();

        var currentTenant = scope.SeedTenant("Current Workspace", "current-workspace");
        var currentDepartment = scope.SeedDepartment("Root", currentTenant.Id);
        var account = scope.SeedAccount("ownerless@finflow.test", "P@ssw0rd!", currentDepartment.Id);
        var currentMembership = scope.SeedMembership(account.Id, currentTenant.Id, RoleType.TenantAdmin);

        await scope.SaveSeedAsync();

        var result = await scope.AuthService.CreateSharedTenantAsync(
            new CreateSharedTenantRequest(account.Id, currentMembership.Id, "New Workspace", "new-workspace", "VND"));

        Assert.True(result.IsSuccess, result.IsFailure ? $"{result.Error.Code}: {result.Error.Description}" : "Expected success.");
        Assert.Equal(account.Id, result.Value.Id);
        Assert.Equal(RoleType.TenantAdmin, result.Value.Role);
        Assert.NotEqual(currentMembership.Id, result.Value.MembershipId);
        Assert.NotEqual(currentTenant.Id, result.Value.IdTenant);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RefreshToken));

        var tenant = await scope.DbContext.Set<Tenant>()
            .SingleAsync(x => x.TenantCode == "new-workspace");

        var membership = await scope.DbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == account.Id && x.IdTenant == tenant.Id);
        var defaultDepartment = await scope.DbContext.Set<Department>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.IdTenant == tenant.Id && x.Name == "Root");

        var refreshToken = await scope.DbContext.Set<RefreshToken>()
            .SingleAsync(x => x.AccountId == account.Id && x.MembershipId == membership.Id);

        Assert.True(membership.IsOwner);
        Assert.Equal(RoleType.TenantAdmin, membership.Role);
        Assert.Equal(tenant.Id, result.Value.IdTenant);
        Assert.Equal(membership.Id, result.Value.MembershipId);
        Assert.True(refreshToken.IsActive);
        Assert.NotNull(defaultDepartment);
        Assert.True(defaultDepartment!.IsActive);
    }

    [Fact]
    public async Task CreateSharedTenant_Fails_WhenCallerAlreadyOwnsAnotherTenant()
    {
        using var scope = _fixture.CreateScope();

        var ownedTenant = scope.SeedTenant("Owned Workspace", "owned-workspace");
        var ownedDepartment = scope.SeedDepartment("Owned Root", ownedTenant.Id);
        var account = scope.SeedAccount("owner@finflow.test", "P@ssw0rd!", ownedDepartment.Id);
        var ownerMembership = scope.SeedMembership(account.Id, ownedTenant.Id, RoleType.TenantAdmin, isOwner: true);

        await scope.SaveSeedAsync();

        var result = await scope.AuthService.CreateSharedTenantAsync(
            new CreateSharedTenantRequest(account.Id, ownerMembership.Id, "Another Workspace", "another-workspace", "VND"));

        Assert.True(result.IsFailure);
        Assert.Equal(TenantErrors.UserAlreadyHasTenant.Code, result.Error.Code);
    }

    [Fact]
    public async Task CreateSharedTenant_Fails_WhenCallerIsNotTenantAdmin()
    {
        using var scope = _fixture.CreateScope();

        var currentTenant = scope.SeedTenant("Current Workspace", "staff-workspace");
        var currentDepartment = scope.SeedDepartment("Root", currentTenant.Id);
        var account = scope.SeedAccount("staff@finflow.test", "P@ssw0rd!", currentDepartment.Id);
        var currentMembership = scope.SeedMembership(account.Id, currentTenant.Id, RoleType.Staff);

        await scope.SaveSeedAsync();

        var result = await scope.AuthService.CreateSharedTenantAsync(
            new CreateSharedTenantRequest(account.Id, currentMembership.Id, "Blocked Workspace", "blocked-workspace", "VND"));

        Assert.True(result.IsFailure);
        Assert.Equal(TenantErrors.Forbidden.Code, result.Error.Code);
    }

    [Fact]
    public async Task CreateSharedTenant_Fails_WhenTenantCodeAlreadyExists()
    {
        using var scope = _fixture.CreateScope();

        var currentTenant = scope.SeedTenant("Current Workspace", "current-shared");
        var currentDepartment = scope.SeedDepartment("Root", currentTenant.Id);
        var account = scope.SeedAccount("duplicate@finflow.test", "P@ssw0rd!", currentDepartment.Id);
        var currentMembership = scope.SeedMembership(account.Id, currentTenant.Id, RoleType.TenantAdmin);
        scope.SeedTenant("Existing Target", "taken-code");

        await scope.SaveSeedAsync();

        var result = await scope.AuthService.CreateSharedTenantAsync(
            new CreateSharedTenantRequest(account.Id, currentMembership.Id, "Duplicate Workspace", "taken-code", "VND"));

        Assert.True(result.IsFailure);
        Assert.Equal(TenantErrors.CodeAlreadyExists.Code, result.Error.Code);
    }
}
