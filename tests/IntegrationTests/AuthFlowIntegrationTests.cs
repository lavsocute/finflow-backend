using FinFlow.Application.Auth.Commands.Register;
using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Application.Membership.Commands.AcceptInvite;
using FinFlow.Application.Membership.Commands.SwitchWorkspace;
using FinFlow.Application.Membership.DTOs.Requests;
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
        var targetTenant = scope.SeedTenant("Target", "target-team");

        var existingAccount = scope.SeedAccount("member@finflow.test", "P@ssw0rd!");
        scope.SeedMembership(existingAccount.Id, sourceTenant.Id, RoleType.Staff);

        var inviterAccount = scope.SeedAccount("admin@finflow.test", "P@ssw0rd!");
        var inviterMembership = scope.SeedMembership(inviterAccount.Id, targetTenant.Id, RoleType.TenantAdmin);

        const string rawInviteToken = "raw-existing-invite-token";
        scope.SeedInvitation(existingAccount.Email, targetTenant.Id, inviterMembership.Id, RoleType.Manager, null, rawInviteToken);

        await scope.SaveSeedAsync();

        var result = await scope.Mediator.Send(
            new AcceptInviteCommand(new AcceptInviteRequest(rawInviteToken, "P@ssw0rd!")));

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
    public async Task AcceptInvite_WithExistingAccount_InvalidPassword_RecordsRateLimitFailure()
    {
        using var scope = _fixture.CreateScope();

        var sourceTenant = scope.SeedTenant("Source", "source-rate-limit");
        var targetTenant = scope.SeedTenant("Target", "target-rate-limit");

        var existingAccount = scope.SeedAccount("member.rate@finflow.test", "P@ssw0rd!");
        scope.SeedMembership(existingAccount.Id, sourceTenant.Id, RoleType.Staff);

        var inviterAccount = scope.SeedAccount("admin.rate@finflow.test", "P@ssw0rd!");
        var inviterMembership = scope.SeedMembership(inviterAccount.Id, targetTenant.Id, RoleType.TenantAdmin);

        const string rawInviteToken = "raw-existing-invalid-password-token";
        scope.SeedInvitation(existingAccount.Email, targetTenant.Id, inviterMembership.Id, RoleType.Manager, null, rawInviteToken);

        await scope.SaveSeedAsync();

        var result = await scope.Mediator.Send(
            new AcceptInviteCommand(new AcceptInviteRequest(rawInviteToken, "WrongPassword!", "127.0.0.1")));

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.InvalidCurrentPassword.Code, result.Error.Code);
        Assert.Single(scope.RateLimiter.RecordedFailures);
        Assert.Equal(("127.0.0.1", existingAccount.Email, targetTenant.Id), scope.RateLimiter.RecordedFailures[0]);
    }

    [Fact]
    public async Task AcceptInvite_WithNewAccount_CreatesAccountMembershipAndTokens()
    {
        using var scope = _fixture.CreateScope();

        var tenant = scope.SeedTenant("Workspace", "workspace-main");
        var inviterAccount = scope.SeedAccount("owner@finflow.test", "P@ssw0rd!");
        var inviterMembership = scope.SeedMembership(inviterAccount.Id, tenant.Id, RoleType.TenantAdmin);

        const string rawInviteToken = "raw-new-user-invite-token";
        scope.SeedInvitation("new.user@finflow.test", tenant.Id, inviterMembership.Id, RoleType.Staff, null, rawInviteToken);

        await scope.SaveSeedAsync();

        var result = await scope.Mediator.Send(
            new AcceptInviteCommand(new AcceptInviteRequest(rawInviteToken, "N3wP@ssword!")));

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
    public async Task AcceptInvite_WithNewAccount_DoesNotRequireDefaultDepartment()
    {
        using var scope = _fixture.CreateScope();

        var tenant = scope.SeedTenant("Workspace", "workspace-no-default-department");
        var inviterAccount = scope.SeedAccount("owner.inactive@finflow.test", "P@ssw0rd!");
        var inviterMembership = scope.SeedMembership(inviterAccount.Id, tenant.Id, RoleType.TenantAdmin);

        const string rawInviteToken = "raw-inactive-department-invite-token";
        scope.SeedInvitation("inactive.department@finflow.test", tenant.Id, inviterMembership.Id, RoleType.Staff, null, rawInviteToken);

        await scope.SaveSeedAsync();

        var result = await scope.Mediator.Send(
            new AcceptInviteCommand(new AcceptInviteRequest(rawInviteToken, "N3wP@ssword!")));

        Assert.True(result.IsSuccess, result.IsFailure ? $"{result.Error.Code}: {result.Error.Description}" : "Expected success.");

        var createdAccount = await scope.DbContext.Set<Account>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Email == "inactive.department@finflow.test");

        var createdMembership = await scope.DbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == createdAccount.Id && x.IdTenant == tenant.Id);

        Assert.Equal(result.Value.Id, createdAccount.Id);
        Assert.Equal(result.Value.MembershipId, createdMembership.Id);
    }

    [Fact]
    public async Task Register_CreatesAccountWithoutAnySeededDepartment()
    {
        using var scope = _fixture.CreateScope();

        var result = await scope.Mediator.Send(
            new RegisterCommand(
                new RegisterRequest(
                    "register.no.department@finflow.test",
                    "Str0ngP@ssword!",
                    "Register Workspace",
                    "127.0.0.1")));

        Assert.True(result.IsSuccess, result.IsFailure ? $"{result.Error.Code}: {result.Error.Description}" : "Expected success.");

        var createdAccount = await scope.DbContext.Set<Account>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Email == "register.no.department@finflow.test");

        Assert.Equal(result.Value.AccountId, createdAccount.Id);
        Assert.Equal("register.no.department@finflow.test", result.Value.Email);
        Assert.True(result.Value.RequiresEmailVerification);
        Assert.Equal(90, result.Value.CooldownSeconds);
        Assert.Single(scope.EmailSender.VerificationEmails);
        Assert.Equal(createdAccount.Email, scope.EmailSender.VerificationEmails.Single().Email);
        Assert.Equal(0, await scope.DbContext.Set<Tenant>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await scope.DbContext.Set<Department>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await scope.DbContext.Set<TenantMembership>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await scope.DbContext.Set<RefreshToken>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SwitchWorkspace_RotatesRefreshTokenAndReturnsNewMembershipContext()
    {
        using var scope = _fixture.CreateScope();

        var tenantA = scope.SeedTenant("Tenant A", "tenant-a");
        var tenantB = scope.SeedTenant("Tenant B", "tenant-b");

        var account = scope.SeedAccount("switch@finflow.test", "P@ssw0rd!");
        var membershipA = scope.SeedMembership(account.Id, tenantA.Id, RoleType.TenantAdmin);
        var membershipB = scope.SeedMembership(account.Id, tenantB.Id, RoleType.Manager);
        const string currentRefreshToken = "current-refresh-token";
        scope.SeedRefreshToken(currentRefreshToken, account.Id, membershipA.Id);

        await scope.SaveSeedAsync();
        scope.CurrentTenant.Id = tenantA.Id;
        scope.CurrentTenant.MembershipId = membershipA.Id;

        var result = await scope.Mediator.Send(
            new SwitchWorkspaceCommand(new SwitchWorkspaceRequest(account.Id, membershipB.Id, currentRefreshToken)));

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
    public async Task SwitchWorkspace_AllowsAccountScopedSession_ToBridgeIntoExistingMembership()
    {
        using var scope = _fixture.CreateScope();

        var tenant = scope.SeedTenant("Bridge Tenant", "bridge-tenant");
        var account = scope.SeedAccount("switch.bridge@finflow.test", "P@ssw0rd!");
        var membership = scope.SeedMembership(account.Id, tenant.Id, RoleType.Staff);
        const string currentRefreshToken = "account-session-refresh-token";
        scope.SeedAccountRefreshToken(currentRefreshToken, account.Id);

        await scope.SaveSeedAsync();

        var result = await scope.Mediator.Send(
            new SwitchWorkspaceCommand(new SwitchWorkspaceRequest(account.Id, membership.Id, currentRefreshToken)));

        Assert.True(result.IsSuccess, result.IsFailure ? $"{result.Error.Code}: {result.Error.Description}" : "Expected success.");
        Assert.Equal("workspace", result.Value.SessionKind);
        Assert.Equal(membership.Id, result.Value.MembershipId);
        Assert.Equal(tenant.Id, result.Value.IdTenant);
        Assert.Equal(RoleType.Staff, result.Value.Role);
        Assert.NotEqual(currentRefreshToken, result.Value.RefreshToken);

        var tokens = await scope.DbContext.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(x => x.AccountId == account.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, tokens.Count);
        Assert.True(tokens[0].IsRevoked);
        Assert.Null(tokens[0].MembershipId);
        Assert.True(tokens[1].IsActive);
        Assert.Equal(membership.Id, tokens[1].MembershipId);
    }

    [Fact]
    public async Task SwitchWorkspace_Fails_WhenRefreshTokenDoesNotBelongToCurrentMembershipContext()
    {
        using var scope = _fixture.CreateScope();

        var tenantA = scope.SeedTenant("Tenant A", "tenant-a-mismatch");
        var tenantB = scope.SeedTenant("Tenant B", "tenant-b-mismatch");

        var account = scope.SeedAccount("switch.mismatch@finflow.test", "P@ssw0rd!");
        var membershipA = scope.SeedMembership(account.Id, tenantA.Id, RoleType.TenantAdmin);
        var membershipB = scope.SeedMembership(account.Id, tenantB.Id, RoleType.Manager);

        const string refreshTokenForMembershipA = "refresh-token-membership-a";
        scope.SeedRefreshToken(refreshTokenForMembershipA, account.Id, membershipA.Id);

        await scope.SaveSeedAsync();
        scope.CurrentTenant.Id = tenantB.Id;
        scope.CurrentTenant.MembershipId = membershipB.Id;

        var result = await scope.Mediator.Send(
            new SwitchWorkspaceCommand(new SwitchWorkspaceRequest(account.Id, membershipA.Id, refreshTokenForMembershipA)));

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.Unauthorized.Code, result.Error.Code);
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
