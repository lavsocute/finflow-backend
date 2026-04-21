using System.Text.Json;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlSubscriptionsApiTests
{
    [Fact]
    public async Task CurrentSubscription_Query_ReturnsTenantScopedSubscriptionAndUsage()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantOne = Tenant.Create("Finance Ops One", "finance-ops-one").Value;
        var tenantTwo = Tenant.Create("Finance Ops Two", "finance-ops-two").Value;

        var tenantOneSubscription = CreateSubscription(tenantOne.Id, PlanTier.Pro, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var tenantTwoSubscription = CreateSubscription(tenantTwo.Id, PlanTier.Enterprise, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));

        var tenantOneUsage = CreateUsageSnapshot(tenantOne.Id, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), ocrPagesUsed: 7, chatbotMessagesUsed: 11, storageUsedBytes: 1_234);
        var tenantTwoUsage = CreateUsageSnapshot(tenantTwo.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1), ocrPagesUsed: 41, chatbotMessagesUsed: 73, storageUsedBytes: 9_876);

        await factory.SeedAsync(db =>
        {
            db.AddRange(
                tenantOne,
                tenantTwo,
                tenantOneSubscription,
                tenantTwoSubscription,
                tenantOneUsage,
                tenantTwoUsage);
        });

        using var tenantOneClient = factory.CreateAuthenticatedClient(
            Guid.NewGuid(),
            "tenant.one@finflow.test",
            RoleType.Staff,
            tenantOne.Id);
        using var tenantTwoClient = factory.CreateAuthenticatedClient(
            Guid.NewGuid(),
            "tenant.two@finflow.test",
            RoleType.Staff,
            tenantTwo.Id);

        const string query = """
            query {
              currentSubscription {
                planTier
                status
                currentPeriodStart
                currentPeriodEnd
                entitlements {
                  documentsManualEntryEnabled
                  documentsOcrEnabled
                  chatbotEnabled
                  storageLimitBytes
                  monthlyOcrPages
                  monthlyChatbotMessages
                }
                usage {
                  ocrPagesUsed
                  chatbotMessagesUsed
                  storageUsedBytes
                }
              }
            }
            """;

        var tenantOnePayload = await GraphQlApiTestFactory.PostGraphQlAsync(tenantOneClient, query);
        var tenantTwoPayload = await GraphQlApiTestFactory.PostGraphQlAsync(tenantTwoClient, query);

        AssertSubscription(
            tenantOnePayload.RootElement.GetProperty("data").GetProperty("currentSubscription"),
            "Pro",
            "Active",
            tenantOneSubscription.PeriodStart,
            tenantOneSubscription.PeriodEnd,
            7,
            11,
            1_234,
            true,
            true,
            10L * 1024 * 1024 * 1024,
            1_000,
            10_000);

        AssertSubscription(
            tenantTwoPayload.RootElement.GetProperty("data").GetProperty("currentSubscription"),
            "Enterprise",
            "Active",
            tenantTwoSubscription.PeriodStart,
            tenantTwoSubscription.PeriodEnd,
            41,
            73,
            9_876,
            true,
            true,
            100L * 1024 * 1024 * 1024,
            10_000,
            100_000);
    }

    private static TenantSubscription CreateSubscription(Guid tenantId, PlanTier planTier, DateTime periodStart, DateTime periodEnd)
    {
        var result = TenantSubscription.Create(tenantId, planTier, periodStart, periodEnd);
        Assert.True(result.IsSuccess, result.Error.Description);
        return result.Value;
    }

    private static TenantUsageSnapshot CreateUsageSnapshot(
        Guid tenantId,
        DateOnly periodStart,
        DateOnly periodEnd,
        int ocrPagesUsed,
        int chatbotMessagesUsed,
        long storageUsedBytes)
    {
        var result = TenantUsageSnapshot.Create(tenantId, periodStart, periodEnd);
        Assert.True(result.IsSuccess, result.Error.Description);

        Assert.True(result.Value.RecordOcrUsage(ocrPagesUsed).IsSuccess);
        Assert.True(result.Value.RecordChatbotUsage(chatbotMessagesUsed).IsSuccess);
        Assert.True(result.Value.SetStorageUsedBytes(storageUsedBytes).IsSuccess);

        return result.Value;
    }

    private static void AssertSubscription(
        JsonElement subscription,
        string planTier,
        string status,
        DateTime currentPeriodStart,
        DateTime currentPeriodEnd,
        int ocrPagesUsed,
        int chatbotMessagesUsed,
        long storageUsedBytes,
        bool documentsManualEntryEnabled,
        bool documentsOcrEnabled,
        long storageLimitBytes,
        int monthlyOcrPages,
        int monthlyChatbotMessages)
    {
        Assert.Equal(planTier, subscription.GetProperty("planTier").GetString());
        Assert.Equal(status, subscription.GetProperty("status").GetString());
        Assert.Equal(currentPeriodStart, subscription.GetProperty("currentPeriodStart").GetDateTime());
        Assert.Equal(currentPeriodEnd, subscription.GetProperty("currentPeriodEnd").GetDateTime());

        var entitlements = subscription.GetProperty("entitlements");
        Assert.Equal(documentsManualEntryEnabled, entitlements.GetProperty("documentsManualEntryEnabled").GetBoolean());
        Assert.Equal(documentsOcrEnabled, entitlements.GetProperty("documentsOcrEnabled").GetBoolean());
        Assert.True(entitlements.GetProperty("chatbotEnabled").GetBoolean());
        Assert.Equal(storageLimitBytes, entitlements.GetProperty("storageLimitBytes").GetInt64());
        Assert.Equal(monthlyOcrPages, entitlements.GetProperty("monthlyOcrPages").GetInt32());
        Assert.Equal(monthlyChatbotMessages, entitlements.GetProperty("monthlyChatbotMessages").GetInt32());

        var usage = subscription.GetProperty("usage");
        Assert.Equal(ocrPagesUsed, usage.GetProperty("ocrPagesUsed").GetInt32());
        Assert.Equal(chatbotMessagesUsed, usage.GetProperty("chatbotMessagesUsed").GetInt32());
        Assert.Equal(storageUsedBytes, usage.GetProperty("storageUsedBytes").GetInt64());
    }
}
