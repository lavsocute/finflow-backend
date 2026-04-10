using System.Text.Json;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlAccountAuthApiTests
{
    [Fact]
    public async Task Register_Mutation_ReturnsAccountSession_WithoutCreatingWorkspaceState()
    {
        await using var factory = new GraphQlApiTestFactory();

        const string mutation = """
            mutation($input: RegisterInput!) {
              register(input: $input) {
                accessToken
                refreshToken
                id
                email
                sessionKind
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new
        {
            input = new
            {
                email = "graphql.register@finflow.test",
                password = "P@ssw0rd!",
                name = "GraphQL Register"
            }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("register");
        Assert.Equal("graphql.register@finflow.test", payload.GetProperty("email").GetString());
        Assert.Equal("account", payload.GetProperty("sessionKind").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("refreshToken").GetString()));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await dbContext.Set<Account>()
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Email == "graphql.register@finflow.test");

        Assert.Equal(0, await dbContext.Set<Tenant>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await dbContext.Set<Department>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await dbContext.Set<TenantMembership>().IgnoreQueryFilters().CountAsync());
        Assert.NotNull(await dbContext.Set<RefreshToken>().IgnoreQueryFilters().SingleAsync(x => x.AccountId == account.Id));
    }

    [Fact]
    public async Task Login_Mutation_ReturnsAccountSession_WithoutRequiringWorkspaceState()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("graphql.login@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        await factory.SeedAsync(db => db.Add(account));

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

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new
        {
            input = new
            {
                email = account.Email,
                password = "P@ssw0rd!"
            }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("login");
        Assert.Equal(account.Email, payload.GetProperty("email").GetString());
        Assert.Equal(account.Id.ToString(), payload.GetProperty("id").GetString());
        Assert.Equal("account", payload.GetProperty("sessionKind").GetString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(0, await dbContext.Set<Tenant>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await dbContext.Set<Department>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await dbContext.Set<TenantMembership>().IgnoreQueryFilters().CountAsync());
        Assert.NotNull(await dbContext.Set<RefreshToken>().IgnoreQueryFilters().SingleAsync(x => x.AccountId == account.Id));
    }

    [Fact]
    public async Task RefreshToken_Mutation_ReturnsAccountSession_ForAccountScopedRefreshToken()
    {
        await using var factory = new GraphQlApiTestFactory();

        var account = Account.Create("graphql.refresh.account@finflow.test", BCrypt.Net.BCrypt.HashPassword("P@ssw0rd!")).Value;
        var refreshToken = RefreshToken.CreateAccountSession("graphql-account-refresh-token", account.Id, 7).Value;

        await factory.SeedAsync(db =>
        {
            db.Add(account);
            db.Add(refreshToken);
        });

        const string mutation = """
            mutation($input: RefreshTokenInput!) {
              refreshToken(input: $input) {
                accessToken
                refreshToken
                id
                email
                sessionKind
                membershipId
                role
                idTenant
              }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(factory.CreateClient(), mutation, new
        {
            input = new
            {
                refreshToken = "graphql-account-refresh-token"
            }
        });

        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());

        var payload = json.RootElement.GetProperty("data").GetProperty("refreshToken");
        Assert.Equal(account.Id.ToString(), payload.GetProperty("id").GetString());
        Assert.Equal(account.Email, payload.GetProperty("email").GetString());
        Assert.Equal("account", payload.GetProperty("sessionKind").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("membershipId").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("role").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("idTenant").ValueKind);
        Assert.NotEqual("graphql-account-refresh-token", payload.GetProperty("refreshToken").GetString());
    }
}
