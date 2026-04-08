using FinFlow.Application.Auth.Dtos;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.IntegrationTests;

public sealed class AuthFlowIntegrationTests
{
    private readonly AuthFlowTestFixture _fixture = new();

    [Fact]
    public async Task AcceptInvite_WithExistingAccount_AddsMembershipAndAcceptsInvitation()
    {
        using var scope = _fixture.CreateScope();

        var sourceTenant = scope.SeedTenant("Source", "source-team");
        var sourceDepartment = scope.SeedDepartment("Source Root", sourceTenant.Id);
        var targetTenant = scope.SeedTenant("Target", "target-team");
        var targetDepartment = scope.SeedDepartment("Target Root", targetTenant.Id);

        var existingAccount = scope.SeedAccount("member@finflow.test", "P@ssw0rd!", sourceDepartment.Id);
        scope.SeedMembership(existingAccount.Id, sourceTenant.Id, RoleType.Staff);

        var inviterAccount = scope.SeedAccount("admin@finflow.test", "P@ssw0rd!", targetDepartment.Id);
        var inviterMembership = scope.SeedMembership(inviterAccount.Id, targetTenant.Id, RoleType.TenantAdmin);

        const string rawInviteToken = "raw-existing-invite-token";
        scope.SeedInvitation(existingAccount.Email, targetTenant.Id, inviterMembership.Id, RoleType.Manager, rawInviteToken);

        await scope.SaveSeedAsync();

        var result = await scope.AuthService.AcceptInviteAsync(
            new AcceptInviteRequest(rawInviteToken, "P@ssw0rd!"));

        Assert.True(result.IsSuccess);
        Assert.Equal(existingAccount.Id, result.Value.Id);
        Assert.Equal(targetTenant.Id, result.Value.IdTenant);
        Assert.Equal(RoleType.Manager, result.Value.Role);

        var memberships = await scope.DbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .Where(x => x.AccountId == existingAccount.Id)
            .ToListAsync();

        Assert.Equal(2, memberships.Count);
        Assert.Contains(memberships, x => x.IdTenant == targetTenant.Id && x.Role == RoleType.Manager);

        var invitation = await scope.DbContext.Set<Invitation>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Email == existingAccount.Email && x.IdTenant == targetTenant.Id);

        Assert.NotNull(invitation.AcceptedAt);
    }

    [Fact]
    public async Task AcceptInvite_WithNewAccount_CreatesAccountMembershipAndTokens()
    {
        using var scope = _fixture.CreateScope();

        var tenant = scope.SeedTenant("Workspace", "workspace-main");
        var department = scope.SeedDepartment("Root", tenant.Id);
        var inviterAccount = scope.SeedAccount("owner@finflow.test", "P@ssw0rd!", department.Id);
        var inviterMembership = scope.SeedMembership(inviterAccount.Id, tenant.Id, RoleType.TenantAdmin);

        const string rawInviteToken = "raw-new-user-invite-token";
        scope.SeedInvitation("new.user@finflow.test", tenant.Id, inviterMembership.Id, RoleType.Staff, rawInviteToken);

        await scope.SaveSeedAsync();

        var result = await scope.AuthService.AcceptInviteAsync(
            new AcceptInviteRequest(rawInviteToken, "N3wP@ssword!"));

        Assert.True(result.IsSuccess);
        Assert.Equal("new.user@finflow.test", result.Value.Email);
        Assert.Equal(tenant.Id, result.Value.IdTenant);
        Assert.Equal(RoleType.Staff, result.Value.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RefreshToken));

        var account = await scope.DbContext.Set<Account>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Email == "new.user@finflow.test");

        var membership = await scope.DbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == account.Id && x.IdTenant == tenant.Id);

        var refreshToken = await scope.DbContext.Set<RefreshToken>()
            .SingleAsync(x => x.AccountId == account.Id && x.MembershipId == membership.Id);

        Assert.Equal(result.Value.Id, account.Id);
        Assert.Equal(result.Value.MembershipId, membership.Id);
        Assert.True(refreshToken.IsActive);
    }

    [Fact]
    public async Task SwitchWorkspace_RotatesRefreshTokenAndReturnsNewMembershipContext()
    {
        using var scope = _fixture.CreateScope();

        var tenantA = scope.SeedTenant("Tenant A", "tenant-a");
        var deptA = scope.SeedDepartment("Root A", tenantA.Id);
        var tenantB = scope.SeedTenant("Tenant B", "tenant-b");
        var deptB = scope.SeedDepartment("Root B", tenantB.Id);

        var account = scope.SeedAccount("switch@finflow.test", "P@ssw0rd!", deptA.Id);
        var membershipA = scope.SeedMembership(account.Id, tenantA.Id, RoleType.TenantAdmin);
        var membershipB = scope.SeedMembership(account.Id, tenantB.Id, RoleType.Manager);
        const string currentRefreshToken = "current-refresh-token";
        scope.SeedRefreshToken(currentRefreshToken, account.Id, membershipA.Id);

        await scope.SaveSeedAsync();

        var result = await scope.AuthService.SwitchWorkspaceAsync(
            new SwitchWorkspaceRequest(account.Id, membershipB.Id, currentRefreshToken));

        Assert.True(result.IsSuccess, result.IsFailure ? $"{result.Error.Code}: {result.Error.Description}" : "Expected success.");
        Assert.Equal(membershipB.Id, result.Value.MembershipId);
        Assert.Equal(tenantB.Id, result.Value.IdTenant);
        Assert.Equal(RoleType.Manager, result.Value.Role);
        Assert.NotEqual(currentRefreshToken, result.Value.RefreshToken);

        var tokens = await scope.DbContext.Set<RefreshToken>()
            .Where(x => x.AccountId == account.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, tokens.Count);
        Assert.False(tokens[0].IsActive);
        Assert.Equal("Workspace switched", tokens[0].ReasonRevoked);
        Assert.True(tokens[1].IsActive);
        Assert.Equal(membershipB.Id, tokens[1].MembershipId);
    }

    [Fact]
    public async Task DepartmentRepository_RespectsTenantIsolation()
    {
        using var scope = _fixture.CreateScope();

        var tenantA = scope.SeedTenant("Tenant A", "isol-a");
        var deptA = scope.SeedDepartment("Finance A", tenantA.Id);
        var tenantB = scope.SeedTenant("Tenant B", "isol-b");
        var deptB = scope.SeedDepartment("Finance B", tenantB.Id);

        await scope.SaveSeedAsync();

        var repository = new DepartmentRepository(scope.DbContext);

        scope.CurrentTenant.Id = tenantA.Id;
        var visibleForTenantA = await repository.GetByIdAsync(deptA.Id);
        var hiddenForTenantA = await repository.GetByIdAsync(deptB.Id);

        scope.CurrentTenant.Id = tenantB.Id;
        var visibleForTenantB = await repository.GetByIdAsync(deptB.Id);
        var hiddenForTenantB = await repository.GetByIdAsync(deptA.Id);

        Assert.NotNull(visibleForTenantA);
        Assert.Null(hiddenForTenantA);
        Assert.NotNull(visibleForTenantB);
        Assert.Null(hiddenForTenantB);
    }
}
