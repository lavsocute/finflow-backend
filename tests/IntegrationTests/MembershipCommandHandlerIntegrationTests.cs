using FinFlow.Application.Membership.Commands.AcceptInvite;
using FinFlow.Application.Membership.Commands.InviteMember;
using FinFlow.Application.Membership.Commands.SwitchWorkspace;
using FinFlow.Application.Membership.DTOs.Requests;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.IntegrationTests;

public sealed class MembershipCommandHandlerIntegrationTests
{
    private readonly AuthFlowTestFixture _fixture = new();

    [Fact]
    public async Task SwitchWorkspaceCommandHandler_RotatesRefreshToken_AndReturnsTargetMembership()
    {
        using var scope = _fixture.CreateScope();

        var tenantA = scope.SeedTenant("Tenant A", "handler-switch-a");
        var deptA = scope.SeedDepartment("Root A", tenantA.Id);
        var tenantB = scope.SeedTenant("Tenant B", "handler-switch-b");
        scope.SeedDepartment("Root B", tenantB.Id);

        var account = scope.SeedAccount("handler.switch@finflow.test", "P@ssw0rd!", deptA.Id);
        var membershipA = scope.SeedMembership(account.Id, tenantA.Id, RoleType.TenantAdmin);
        var membershipB = scope.SeedMembership(account.Id, tenantB.Id, RoleType.Manager);
        const string rawRefreshToken = "handler-switch-refresh-token";
        scope.SeedRefreshToken(rawRefreshToken, account.Id, membershipA.Id);

        await scope.SaveSeedAsync();
        scope.CurrentTenant.Id = tenantA.Id;
        scope.CurrentTenant.MembershipId = membershipA.Id;

        var handler = scope.CreateSwitchWorkspaceHandler();

        var result = await handler.Handle(
            new SwitchWorkspaceCommand(new SwitchWorkspaceRequest(account.Id, membershipB.Id, rawRefreshToken)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(membershipB.Id, result.Value.MembershipId);
        Assert.Equal(tenantB.Id, result.Value.IdTenant);

        var refreshTokens = await scope.DbContext.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(x => x.AccountId == account.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, refreshTokens.Count);
        Assert.False(refreshTokens[0].IsActive);
        Assert.True(refreshTokens[1].IsActive);
        Assert.Equal(membershipB.Id, refreshTokens[1].MembershipId);
    }

    [Fact]
    public async Task InviteMemberCommandHandler_CreatesInvitation_AndReturnsRawToken()
    {
        using var scope = _fixture.CreateScope();

        var tenant = scope.SeedTenant("Workspace", "handler-invite");
        var department = scope.SeedDepartment("Root", tenant.Id);
        var inviterAccount = scope.SeedAccount("handler.inviter@finflow.test", "P@ssw0rd!", department.Id);
        var inviterMembership = scope.SeedMembership(inviterAccount.Id, tenant.Id, RoleType.TenantAdmin);

        await scope.SaveSeedAsync();
        scope.CurrentTenant.Id = tenant.Id;
        scope.CurrentTenant.MembershipId = inviterMembership.Id;

        var handler = scope.CreateInviteMemberHandler();

        var result = await handler.Handle(
            new InviteMemberCommand(new InviteMemberRequest(inviterAccount.Id, inviterMembership.Id, "invited.user@finflow.test", RoleType.Staff)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.InviteToken));
        Assert.Equal("invited.user@finflow.test", result.Value.Email);

        var invitation = await scope.DbContext.Set<Invitation>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Email == "invited.user@finflow.test" && x.IdTenant == tenant.Id);

        Assert.Null(invitation.AcceptedAt);
        Assert.Null(invitation.RevokedAt);
    }

    [Fact]
    public async Task AcceptInviteCommandHandler_ForExistingAccount_AddsMembership_AndIssuesTokens()
    {
        using var scope = _fixture.CreateScope();

        var sourceTenant = scope.SeedTenant("Source", "handler-accept-source");
        var sourceDepartment = scope.SeedDepartment("Source Root", sourceTenant.Id);
        var targetTenant = scope.SeedTenant("Target", "handler-accept-target");
        var targetDepartment = scope.SeedDepartment("Target Root", targetTenant.Id);

        var existingAccount = scope.SeedAccount("handler.accept@finflow.test", "P@ssw0rd!", sourceDepartment.Id);
        scope.SeedMembership(existingAccount.Id, sourceTenant.Id, RoleType.Staff);

        var inviterAccount = scope.SeedAccount("handler.admin@finflow.test", "P@ssw0rd!", targetDepartment.Id);
        var inviterMembership = scope.SeedMembership(inviterAccount.Id, targetTenant.Id, RoleType.TenantAdmin);

        const string rawInviteToken = "handler-accept-raw-token";
        scope.SeedInvitation(existingAccount.Email, targetTenant.Id, inviterMembership.Id, RoleType.Manager, rawInviteToken);

        await scope.SaveSeedAsync();

        var handler = scope.CreateAcceptInviteHandler();

        var result = await handler.Handle(
            new AcceptInviteCommand(new AcceptInviteRequest(rawInviteToken, "P@ssw0rd!")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existingAccount.Id, result.Value.Id);
        Assert.Equal(targetTenant.Id, result.Value.IdTenant);
        Assert.Equal(RoleType.Manager, result.Value.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RefreshToken));

        var memberships = await scope.DbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .Where(x => x.AccountId == existingAccount.Id)
            .ToListAsync();

        Assert.Equal(2, memberships.Count);
        Assert.Contains(memberships, x => x.IdTenant == targetTenant.Id && x.Role == RoleType.Manager);
    }
}
