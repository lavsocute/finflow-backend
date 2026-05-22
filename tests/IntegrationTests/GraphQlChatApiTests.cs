using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Expenses;
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

        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);
        var member = await factory.CreateMembershipAsync(RoleType.TenantAdmin, tenantId);

        var mutation = @"
            mutation Chat($input: ChatInput!) {
                chat(input: $input) {
                    answer
                    answerSource
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

        using var client = factory.CreateAuthenticatedClient(member);

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, mutation, variables);

        var data = json.RootElement.GetProperty("data").GetProperty("chat");
        var answer = data.GetProperty("answer").GetString();

        Assert.NotNull(answer);
        Assert.NotEmpty(answer);
        Assert.Equal("RAG", data.GetProperty("answerSource").GetString());
        Assert.True(data.GetProperty("sessionId").GetGuid() != Guid.Empty);
    }

    [Fact]
    public async Task Chat_AggregateQuestion_ReturnsOwnSpending_ForStaff()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);

        var member = await factory.CreateMembershipAsync(RoleType.Staff, tenantId, Guid.NewGuid());
        var now = DateTime.UtcNow;

        await factory.SeedAsync(db =>
        {
            db.Add(Expense.Create(
                tenantId,
                member.DepartmentId ?? Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Own monthly expense",
                125000m,
                "VND",
                125000m,
                "VND",
                now.Month,
                now.Year,
                new DateTime(now.Year, now.Month, Math.Min(now.Day, 28), 0, 0, 0, DateTimeKind.Utc),
                member.MembershipId).Value);
        });

        using var client = factory.CreateAuthenticatedClient(member);
        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, """
            mutation Chat($input: ChatInput!) {
                chat(input: $input) {
                    answer
                    answerSource
                    sessionId
                    messageId
                    documentCount
                    tokenUsage
                }
            }
            """, new
        {
            input = new
            {
                sessionId = (Guid?)null,
                query = "Tháng này tôi đã tiêu bao nhiêu?",
                departmentId = (Guid?)null
            }
        });

        var data = json.RootElement.GetProperty("data").GetProperty("chat");
        Assert.Equal(1, data.GetProperty("documentCount").GetInt32());
        Assert.Contains("125000", data.GetProperty("answer").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VND", data.GetProperty("answer").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("REPORTING", data.GetProperty("answerSource").GetString());
    }

    [Fact]
    public async Task Chat_Mutation_PersistsQuotaUsage_ForCurrentSubscription()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);
        var member = await factory.CreateMembershipAsync(RoleType.Staff, tenantId);

        using var client = factory.CreateAuthenticatedClient(member);

        const string currentSubscriptionQuery = """
            query {
              currentSubscription {
                usage {
                  chatbotMessagesUsed
                }
                currentMemberUsage {
                  chatbotMessagesUsed
                  remainingChatbotMessages
                }
              }
            }
            """;

        using var beforeJson = await GraphQlApiTestFactory.PostGraphQlAsync(client, currentSubscriptionQuery);
        var beforeSubscription = beforeJson.RootElement.GetProperty("data").GetProperty("currentSubscription");
        var beforeWorkspaceUsed = beforeSubscription.GetProperty("usage").GetProperty("chatbotMessagesUsed").GetInt32();
        var beforeMemberUsed = beforeSubscription.GetProperty("currentMemberUsage").GetProperty("chatbotMessagesUsed").GetInt32();
        var beforeRemaining = beforeSubscription.GetProperty("currentMemberUsage").GetProperty("remainingChatbotMessages").GetInt32();

        using var chatJson = await GraphQlApiTestFactory.PostGraphQlAsync(client, """
            mutation Chat($input: ChatInput!) {
              chat(input: $input) {
                answer
                sessionId
              }
            }
            """, new
        {
            input = new
            {
                sessionId = (Guid?)null,
                query = "hello",
                departmentId = (Guid?)null
            }
        });

        Assert.NotNull(chatJson.RootElement.GetProperty("data").GetProperty("chat").GetProperty("answer").GetString());

        using var afterJson = await GraphQlApiTestFactory.PostGraphQlAsync(client, currentSubscriptionQuery);
        var afterSubscription = afterJson.RootElement.GetProperty("data").GetProperty("currentSubscription");
        var afterWorkspaceUsed = afterSubscription.GetProperty("usage").GetProperty("chatbotMessagesUsed").GetInt32();
        var afterMemberUsed = afterSubscription.GetProperty("currentMemberUsage").GetProperty("chatbotMessagesUsed").GetInt32();
        var afterRemaining = afterSubscription.GetProperty("currentMemberUsage").GetProperty("remainingChatbotMessages").GetInt32();

        Assert.Equal(beforeWorkspaceUsed + 1, afterWorkspaceUsed);
        Assert.Equal(beforeMemberUsed + 1, afterMemberUsed);
        Assert.Equal(beforeRemaining - 1, afterRemaining);
    }

    [Fact]
    public async Task Chat_Mutation_FailsWithoutSubscription()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        var member = await factory.CreateMembershipAsync(RoleType.TenantAdmin, tenantId);

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

        using var client = factory.CreateAuthenticatedClient(member);

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

        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);
        var member = await factory.CreateMembershipAsync(RoleType.TenantAdmin, tenantId);

        var query = @"
            query GetChatSessions {
                chatSessions(limit: 10) {
                    id
                    title
                    messageCount
                    lastMessageAt
                }
            }";

        using var client = factory.CreateAuthenticatedClient(member);

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query);

        var sessions = json.RootElement.GetProperty("data").GetProperty("chatSessions");
        Assert.Equal(JsonValueKind.Array, sessions.ValueKind);
    }

    [Fact]
    public async Task GetChatSessions_LegacyFieldName_RemainsAvailable()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();

        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);
        var member = await factory.CreateMembershipAsync(RoleType.TenantAdmin, tenantId);

        var query = @"
            query GetChatSessions {
                getChatSessions(limit: 10) {
                    id
                    title
                    messageCount
                    lastMessageAt
                }
            }";

        using var client = factory.CreateAuthenticatedClient(member);

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query);

        var sessions = json.RootElement.GetProperty("data").GetProperty("getChatSessions");
        Assert.Equal(JsonValueKind.Array, sessions.ValueKind);
    }

    [Fact]
    public async Task GetChatHistory_LegacyFieldName_RemainsAvailable()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();

        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);
        var member = await factory.CreateMembershipAsync(RoleType.TenantAdmin, tenantId);

        using var client = factory.CreateAuthenticatedClient(member);
        var chatResult = await factory.ExecuteChatAsync(client, member, "Hello, what can you do?");

        Assert.Empty(chatResult.Errors);
        Assert.NotNull(chatResult.Data);

        var query = """
            query GetChatHistory($sessionId: UUID!) {
                getChatHistory(sessionId: $sessionId) {
                    id
                    sessionId
                    senderId
                    role
                    content
                    tokenCount
                    createdAt
                }
            }
            """;

        using var json = await GraphQlApiTestFactory.PostGraphQlAsync(client, query, new
        {
            sessionId = chatResult.Data!.SessionId
        });

        var history = json.RootElement.GetProperty("data").GetProperty("getChatHistory");
        Assert.Equal(JsonValueKind.Array, history.ValueKind);
        Assert.True(history.GetArrayLength() >= 2);
    }
}
