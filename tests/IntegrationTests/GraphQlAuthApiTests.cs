using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlAuthApiTests
{
    [Fact]
    public async Task Login_Mutation_Works_ThroughHttpPipeline()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Login Workspace", "http-login").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var account = Account.Create("login.http@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        account.MarkEmailVerified(DateTime.UtcNow);

        await factory.SeedAsync(db =>
        {
            db.Add(tenant);
            db.Add(department);
            db.Add(account);
        });

        using var client = factory.CreateClient();
        const string mutation = """
            mutation($input: LoginInput!) {
              login(input: $input) {
                accessToken
                refreshToken
                id
                email
                sessionKind
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, new
        {
            input = new { email = account.Email, password = "P@ssw0rd!" }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("login");
        Assert.Equal(account.Email, payload.GetProperty("email").GetString());
        Assert.Equal(account.Id.ToString(), payload.GetProperty("id").GetString());
        Assert.Equal("account", payload.GetProperty("sessionKind").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("refreshToken").GetString()));
        Assert.False(payload.TryGetProperty("membershipId", out _));
        Assert.False(payload.TryGetProperty("idTenant", out _));
        Assert.False(payload.TryGetProperty("role", out _));
    }

    [Fact]
    public async Task InviteMember_Mutation_Works_ThroughHttpPipeline()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Invite Workspace", "http-invite").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var inviter = Account.Create("invite.admin@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var inviterMembership = TenantMembership.Create(inviter.Id, tenant.Id, RoleType.TenantAdmin, isOwner: true).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(tenant);
            db.Add(department);
            db.Add(inviter);
            db.Add(inviterMembership);
        });

        using var client = factory.CreateAuthenticatedClient(
            inviter.Id,
            inviter.Email,
            RoleType.TenantAdmin,
            tenant.Id,
            inviterMembership.Id);

        const string mutation = """
            mutation($input: InviteMemberInput!) {
              inviteMember(input: $input) {
                invitationId
                inviteToken
                email
                role
                idTenant
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, new
        {
            input = new { email = "new.member@finflow.test", role = "ACCOUNTANT" }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("inviteMember");
        Assert.Equal("new.member@finflow.test", payload.GetProperty("email").GetString());
        Assert.Equal("ACCOUNTANT", payload.GetProperty("role").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("inviteToken").GetString()));
    }

    [Fact]
    public async Task AcceptInvite_Mutation_Works_ThroughHttpPipeline()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Accept Workspace", "http-accept").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var inviter = Account.Create("accept.admin@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var inviterMembership = TenantMembership.Create(inviter.Id, tenant.Id, RoleType.TenantAdmin, isOwner: true).Value;
        const string rawInviteToken = "raw-http-invite-token";
        var invitation = Invitation.Create(
            "accepted.member@finflow.test",
            tenant.Id,
            inviterMembership.Id,
            RoleType.Accountant,
            rawInviteToken,
            DateTime.UtcNow.AddDays(7)).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(tenant);
            db.Add(department);
            db.Add(inviter);
            db.Add(inviterMembership);
            db.Add(invitation);
        });

        using var client = factory.CreateClient();
        const string mutation = """
            mutation($input: AcceptInviteInput!) {
              acceptInvite(input: $input) {
                id
                membershipId
                idTenant
                email
                role
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, new
        {
            input = new { inviteToken = rawInviteToken, password = "N3wP@ssw0rd!" }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("acceptInvite");
        Assert.Equal("accepted.member@finflow.test", payload.GetProperty("email").GetString());
        Assert.Equal("ACCOUNTANT", payload.GetProperty("role").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var acceptedMember = await dbContext.Set<Account>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Email == "accepted.member@finflow.test");
        var createdMembership = await dbContext.Set<TenantMembership>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == acceptedMember.Id && x.IdTenant == tenant.Id);

        Assert.Equal(RoleType.Accountant, createdMembership.Role);
    }

    [Fact]
    public async Task ChangePassword_Mutation_Works_ThroughHttpPipeline()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Password Workspace", "http-password").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var account = Account.Create("password.http@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin).Value;

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

        const string mutation = """
            mutation($input: ChangePasswordInput!) {
              changePassword(input: $input)
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, new
        {
            input = new { currentPassword = "P@ssw0rd!", newPassword = "N3wP@ssw0rd!" }
        });

        Assert.True(json.RootElement.GetProperty("data").GetProperty("changePassword").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updatedAccount = await dbContext.Set<Account>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == account.Id);

        Assert.True(BCrypt.Net.BCrypt.Verify("N3wP@ssw0rd!", updatedAccount.PasswordHash));
    }

    [Fact]
    public async Task Logout_Mutation_Works_ThroughHttpPipeline()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenant = Tenant.Create("Logout Workspace", "http-logout").Value;
        var department = Department.Create("Root", tenant.Id).Value;
        var account = Account.Create("logout.http@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var membership = TenantMembership.Create(account.Id, tenant.Id, RoleType.TenantAdmin).Value;
        const string rawRefreshToken = "logout-http-raw-token";
        var refreshToken = RefreshToken.Create(rawRefreshToken, account.Id, membership.Id, 7).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(tenant);
            db.Add(department);
            db.Add(account);
            db.Add(membership);
            db.Add(refreshToken);
        });

        using var client = factory.CreateAuthenticatedClient(
            account.Id,
            account.Email,
            RoleType.TenantAdmin,
            tenant.Id,
            membership.Id);

        const string mutation = """
            mutation($refreshToken: String!) {
              logout(refreshToken: $refreshToken)
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, new
        {
            refreshToken = rawRefreshToken
        });

        Assert.True(json.RootElement.GetProperty("data").GetProperty("logout").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var revokedToken = await dbContext.Set<RefreshToken>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.AccountId == account.Id && x.MembershipId == membership.Id);

        Assert.True(revokedToken.IsRevoked);
    }
}
