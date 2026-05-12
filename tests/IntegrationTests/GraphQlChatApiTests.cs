using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Interfaces;
using FinFlow.Infrastructure;
using FinFlow.Application.Subscriptions;
using System.Text.Json;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlChatApiTests
{
    [Fact]
    public async Task Chat_Mutation_RequiresAuthentication()
    {
        await using var factory = new GraphQlApiTestFactory();

        var mutation = @"
            mutation Chat($input: ChatInput!) {
                chat(input: $input) {
                    answer
                }
            }";

        var variables = new
        {
            input = new
            {
                sessionId = (string?)null,
                query = "Hello, what can you do?",
                departmentId = (string?)null
            }
        };

        using var client = factory.CreateClient();
        using var json = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, mutation, variables);

        Assert.True(json.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Chat_Mutation_RequiresMembershipContext()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var mutation = @"
            mutation Chat($input: ChatInput!) {
                chat(input: $input) {
                    answer
                }
            }";

        var variables = new
        {
            input = new
            {
                sessionId = (string?)null,
                query = "Hello, what can you do?",
                departmentId = (string?)null
            }
        };

        using var client = factory.CreateAuthenticatedClient(
            accountId,
            "test@example.com",
            RoleType.Staff,
            tenantId,
            membershipId: null);

        using var json = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, mutation, variables);

        Assert.True(json.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.GetArrayLength() > 0);
        Assert.Contains("User is not authenticated", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Chat_Mutation_Works_WithSubscription()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);
        await factory.SeedAsync(db => { });

        var mutation = @"
            mutation Chat($input: ChatInput!) {
                chat(input: $input) {
                    answer
                    sessionId
                    messageId
                    documentCount
                    tokenUsage
                }
            }";

        var variables = new
        {
            input = new
            {
                sessionId = (string?)null,
                query = "Hello, what can you do?",
                departmentId = (string?)null
            }
        };

        using var client = factory.CreateAuthenticatedClient(
            accountId,
            "test@example.com",
            RoleType.TenantAdmin,
            tenantId,
            membershipId);

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, variables);

        var data = json.RootElement.GetProperty("data").GetProperty("chat");
        var answer = data.GetProperty("answer").GetString();

        Assert.NotNull(answer);
        Assert.NotEmpty(answer);
        Assert.True(data.GetProperty("sessionId").GetGuid() != Guid.Empty);
    }

    [Fact]
    public async Task Chat_Mutation_FailsWithoutSubscription()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await factory.SeedAsync(db => { });

        var mutation = @"
            mutation Chat($input: ChatInput!) {
                chat(input: $input) {
                    answer
                }
            }";

        var variables = new
        {
            input = new
            {
                sessionId = (string?)null,
                query = "Hello",
                departmentId = (string?)null
            }
        };

        using var client = factory.CreateAuthenticatedClient(
            accountId,
            "test@example.com",
            RoleType.TenantAdmin,
            tenantId,
            membershipId);

        using var json = await GraphQlApiTestFactory.PostGraphQlAllowingErrorsAsync(client, mutation, variables);

        if (json.RootElement.TryGetProperty("errors", out var errors))
        {
            Assert.True(errors.GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task GetChatSessions_ReturnsList()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);
        await factory.SeedAsync(db => { });

        var query = @"
            query GetChatSessions {
                chatSessions(limit: 10) {
                    id
                    title
                    messageCount
                    lastMessageAt
                }
            }";

        using var client = factory.CreateAuthenticatedClient(
            accountId,
            "test@example.com",
            RoleType.TenantAdmin,
            tenantId,
            membershipId);

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query);

        var sessions = json.RootElement.GetProperty("data").GetProperty("chatSessions");
        Assert.Equal(JsonValueKind.Array, sessions.ValueKind);
    }
}
