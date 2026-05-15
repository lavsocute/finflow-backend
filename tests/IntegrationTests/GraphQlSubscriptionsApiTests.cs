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

        await factory.SeedAsync(db =>
        {
            db.AddRange(
                tenantOne,
                tenantTwo);
        });

        var tenantOneMembership = await factory.CreateMembershipAsync(RoleType.Staff, tenantOne.Id);
        var tenantTwoMembership = await factory.CreateMembershipAsync(RoleType.Staff, tenantTwo.Id);

        // Use dynamic periods so subscriptions are Active during test execution.
        var nowDate = DateTime.UtcNow.Date;
        var periodStartUtc = DateTime.SpecifyKind(nowDate.AddDays(-1), DateTimeKind.Utc);
        var periodEndUtc = periodStartUtc.AddMonths(1);
        var periodStartDate = DateOnly.FromDateTime(periodStartUtc);
        var periodEndDate = DateOnly.FromDateTime(periodEndUtc);

        var tenantOneSubscription = CreateSubscription(tenantOne.Id, PlanTier.Pro, periodStartUtc, periodEndUtc);
        var tenantTwoSubscription = CreateSubscription(tenantTwo.Id, PlanTier.Enterprise, periodStartUtc, periodEndUtc);

        var tenantOneUsage = CreateUsageSnapshot(tenantOne.Id, periodStartDate, periodEndDate, ocrPagesUsed: 7, chatbotMessagesUsed: 11, storageUsedBytes: 1_234);
        var tenantTwoUsage = CreateUsageSnapshot(tenantTwo.Id, periodStartDate, periodEndDate, ocrPagesUsed: 41, chatbotMessagesUsed: 73, storageUsedBytes: 9_876);
        var tenantOneMemberUsage = CreateMemberUsageSnapshot(tenantOne.Id, tenantOneMembership.MembershipId, periodStartDate, periodEndDate, ocrPagesUsed: 12, chatbotMessagesUsed: 34);
        var tenantTwoMemberUsage = CreateMemberUsageSnapshot(tenantTwo.Id, tenantTwoMembership.MembershipId, periodStartDate, periodEndDate, ocrPagesUsed: 120, chatbotMessagesUsed: 345);

        await factory.SeedAsync(db =>
        {
            db.AddRange(
                tenantOneSubscription,
                tenantTwoSubscription,
                tenantOneUsage,
                tenantTwoUsage,
                tenantOneMemberUsage,
                tenantTwoMemberUsage);
        });

        using var tenantOneClient = factory.CreateAuthenticatedClient(tenantOneMembership);
        using var tenantTwoClient = factory.CreateAuthenticatedClient(tenantTwoMembership);

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
                  workspaceMonthlyOcrPages
                  memberMonthlyOcrPages
                  workspaceMonthlyChatbotMessages
                  memberMonthlyChatbotMessages
                }
                usage {
                  ocrPagesUsed
                  chatbotMessagesUsed
                  storageUsedBytes
                }
                currentMemberUsage {
                  ocrPagesUsed
                  chatbotMessagesUsed
                  remainingOcrPages
                  remainingChatbotMessages
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
            12,
            34,
            88,
            466,
            true,
            true,
            10L * 1024 * 1024 * 1024,
            1_000,
            100,
            10_000,
            500);

        AssertSubscription(
            tenantTwoPayload.RootElement.GetProperty("data").GetProperty("currentSubscription"),
            "Enterprise",
            "Active",
            tenantTwoSubscription.PeriodStart,
            tenantTwoSubscription.PeriodEnd,
            41,
            73,
            9_876,
            120,
            345,
            880,
            4_655,
            true,
            true,
            100L * 1024 * 1024 * 1024,
            10_000,
            1_000,
            100_000,
            5_000);
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

    private static MemberUsageSnapshot CreateMemberUsageSnapshot(
        Guid tenantId,
        Guid membershipId,
        DateOnly periodStart,
        DateOnly periodEnd,
        int ocrPagesUsed,
        int chatbotMessagesUsed)
    {
        var result = MemberUsageSnapshot.Create(tenantId, membershipId, periodStart, periodEnd);
        Assert.True(result.IsSuccess, result.Error.Description);

        Assert.True(result.Value.RecordOcrUsage(ocrPagesUsed).IsSuccess);
        Assert.True(result.Value.RecordChatbotUsage(chatbotMessagesUsed).IsSuccess);

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
        int memberOcrPagesUsed,
        int memberChatbotMessagesUsed,
        int remainingMemberOcrPages,
        int remainingMemberChatbotMessages,
        bool documentsManualEntryEnabled,
        bool documentsOcrEnabled,
        long storageLimitBytes,
        int workspaceMonthlyOcrPages,
        int memberMonthlyOcrPages,
        int workspaceMonthlyChatbotMessages,
        int memberMonthlyChatbotMessages)
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
        Assert.Equal(workspaceMonthlyOcrPages, entitlements.GetProperty("workspaceMonthlyOcrPages").GetInt32());
        Assert.Equal(memberMonthlyOcrPages, entitlements.GetProperty("memberMonthlyOcrPages").GetInt32());
        Assert.Equal(workspaceMonthlyChatbotMessages, entitlements.GetProperty("workspaceMonthlyChatbotMessages").GetInt32());
        Assert.Equal(memberMonthlyChatbotMessages, entitlements.GetProperty("memberMonthlyChatbotMessages").GetInt32());

        var usage = subscription.GetProperty("usage");
        Assert.Equal(ocrPagesUsed, usage.GetProperty("ocrPagesUsed").GetInt32());
        Assert.Equal(chatbotMessagesUsed, usage.GetProperty("chatbotMessagesUsed").GetInt32());
        Assert.Equal(storageUsedBytes, usage.GetProperty("storageUsedBytes").GetInt64());

        var currentMemberUsage = subscription.GetProperty("currentMemberUsage");
        Assert.Equal(memberOcrPagesUsed, currentMemberUsage.GetProperty("ocrPagesUsed").GetInt32());
        Assert.Equal(memberChatbotMessagesUsed, currentMemberUsage.GetProperty("chatbotMessagesUsed").GetInt32());
        Assert.Equal(remainingMemberOcrPages, currentMemberUsage.GetProperty("remainingOcrPages").GetInt32());
        Assert.Equal(remainingMemberChatbotMessages, currentMemberUsage.GetProperty("remainingChatbotMessages").GetInt32());
    }
}
