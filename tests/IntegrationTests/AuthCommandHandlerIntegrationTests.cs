using FinFlow.Application.Auth.Commands.ChangePassword;
using FinFlow.Application.Auth.Commands.Login;
using FinFlow.Application.Auth.Commands.Logout;
using FinFlow.Application.Auth.Commands.RefreshToken;
using FinFlow.Application.Auth.Commands.Register;
using FinFlow.Application.Auth.DTOs.Requests;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.IntegrationTests;

public sealed class AuthCommandHandlerIntegrationTests
{
    private readonly AuthFlowTestFixture _fixture = new();

    [Fact]
    public async Task LoginCommandHandler_ReturnsTokens_ForValidCredentials()
    {
        using var scope = _fixture.CreateScope();

        var tenant = scope.SeedTenant("Workspace", "handler-login");
        var department = scope.SeedDepartment("Root", tenant.Id);
        var account = scope.SeedAccount("handler.login@finflow.test", "P@ssw0rd!", department.Id);
        var membership = scope.SeedMembership(account.Id, tenant.Id, RoleType.TenantAdmin);

        await scope.SaveSeedAsync();

        var handler = scope.CreateLoginHandler();

        var result = await handler.Handle(
            new LoginCommand(new LoginRequest(account.Email, "P@ssw0rd!", tenant.TenantCode, "127.0.0.1")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(account.Email, result.Value.Email);
        Assert.Equal(membership.Id, result.Value.MembershipId);
        Assert.Equal(tenant.Id, result.Value.IdTenant);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RefreshToken));
    }

    [Fact]
    public async Task RegisterCommandHandler_CreatesTenantDepartmentMembershipAndTokens()
    {
        using var scope = _fixture.CreateScope();
        var handler = scope.CreateRegisterHandler();

        var result = await handler.Handle(
            new RegisterCommand(new RegisterRequest("handler.register@finflow.test", "P@ssw0rd!", "FinFlow Team", "handler-register", "Root", "127.0.0.1")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var account = await scope.DbContext.Set<Account>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Email == "handler.register@finflow.test");
        var membership = await scope.DbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == account.Id);
        var tenant = await scope.DbContext.Set<Tenant>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == membership.IdTenant);
        var department = await scope.DbContext.Set<Department>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.IdTenant == tenant.Id);

        Assert.Equal("handler-register", tenant.TenantCode);
        Assert.Equal("Root", department.Name);
        Assert.True(membership.IsOwner);
    }

    [Fact]
    public async Task RefreshTokenCommandHandler_RotatesToken_ForActiveAccount()
    {
        using var scope = _fixture.CreateScope();

        var tenant = scope.SeedTenant("Workspace", "handler-refresh");
        var department = scope.SeedDepartment("Root", tenant.Id);
        var account = scope.SeedAccount("handler.refresh@finflow.test", "P@ssw0rd!", department.Id);
        var membership = scope.SeedMembership(account.Id, tenant.Id, RoleType.TenantAdmin);
        const string rawRefreshToken = "handler-refresh-token";
        scope.SeedRefreshToken(rawRefreshToken, account.Id, membership.Id);

        await scope.SaveSeedAsync();

        var handler = scope.CreateRefreshTokenHandler();

        var result = await handler.Handle(new RefreshTokenCommand(new RefreshTokenRequest(rawRefreshToken)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(membership.Id, result.Value.MembershipId);
        Assert.NotEqual(rawRefreshToken, result.Value.RefreshToken);
    }

    [Fact]
    public async Task ChangePasswordCommandHandler_RevokesAllRefreshTokens()
    {
        using var scope = _fixture.CreateScope();

        var tenant = scope.SeedTenant("Workspace", "handler-change-password");
        var department = scope.SeedDepartment("Root", tenant.Id);
        var account = scope.SeedAccount("handler.password@finflow.test", "P@ssw0rd!", department.Id);
        var membership = scope.SeedMembership(account.Id, tenant.Id, RoleType.TenantAdmin);
        scope.SeedRefreshToken("handler-password-token-1", account.Id, membership.Id);
        scope.SeedRefreshToken("handler-password-token-2", account.Id, membership.Id);

        await scope.SaveSeedAsync();

        var handler = scope.CreateChangePasswordHandler();

        var result = await handler.Handle(
            new ChangePasswordCommand(new ChangePasswordRequest(account.Id, "P@ssw0rd!", "N3wP@ssword!")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var refreshTokens = await scope.DbContext.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .Where(x => x.AccountId == account.Id)
            .ToListAsync();

        Assert.All(refreshTokens, token =>
        {
            Assert.True(token.IsRevoked);
            Assert.Equal("Password changed", token.ReasonRevoked);
        });
    }

    [Fact]
    public async Task LogoutCommandHandler_RevokesRefreshToken()
    {
        using var scope = _fixture.CreateScope();

        var tenant = scope.SeedTenant("Workspace", "handler-logout");
        var department = scope.SeedDepartment("Root", tenant.Id);
        var account = scope.SeedAccount("handler.logout@finflow.test", "P@ssw0rd!", department.Id);
        var membership = scope.SeedMembership(account.Id, tenant.Id, RoleType.TenantAdmin);
        const string rawRefreshToken = "handler-logout-token";
        scope.SeedRefreshToken(rawRefreshToken, account.Id, membership.Id);

        await scope.SaveSeedAsync();

        var handler = scope.CreateLogoutHandler();

        var result = await handler.Handle(new LogoutCommand(new LogoutRequest(rawRefreshToken)), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var token = await scope.DbContext.Set<RefreshToken>()
            .SingleAsync(x => x.AccountId == account.Id);

        Assert.True(token.IsRevoked);
        Assert.Equal("User logout", token.ReasonRevoked);
    }
}
