using FinFlow.Application.Subscriptions;
using FinFlow.Domain.Enums;

namespace FinFlow.IntegrationTests;

public sealed class GraphQlChatSecurityApiTests
{
    [Fact]
    public async Task Chat_Mutation_Denies_Staff_Using_Another_Members_SessionId()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);

        var staffA = await factory.CreateMembershipAsync(RoleType.Staff, tenantId, departmentId);
        var staffB = await factory.CreateMembershipAsync(RoleType.Staff, tenantId, departmentId);
        var sessionId = await factory.CreateChatSessionAsync(staffB);

        using var client = factory.CreateClient();
        var result = await factory.ExecuteChatAsync(client, staffA, "Show me my expenses", sessionId);

        Assert.Single(result.Errors);
        Assert.Contains("access denied", result.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Chat_Mutation_DoesNotRetrieve_Other_Staff_Expense_Data()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);

        var staffA = await factory.CreateMembershipAsync(RoleType.Staff, tenantId, departmentId);
        var staffB = await factory.CreateMembershipAsync(RoleType.Staff, tenantId, departmentId);
        await factory.IndexReviewedExpenseAsync(staffB, "Other Staff Merchant");

        using var client = factory.CreateClient();
        var result = await factory.ExecuteChatAsync(client, staffA, "Show me Other Staff Merchant expense");

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data!.DocumentCount);
        Assert.Contains("not enough authorized context", result.Data.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chat_Mutation_Denies_Unknown_Membership()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        const string email = "unknown-membership@finflow.test";

        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);

        using var client = factory.CreateAuthenticatedClient(
            accountId,
            email,
            RoleType.Staff,
            tenantId,
            membershipId);

        var payload = await factory.ExecuteChatAsync(
            client,
            new GraphQlApiTestFactory.TestMembership(
                accountId,
                membershipId,
                tenantId,
                null,
                RoleType.Staff,
                email),
            "Show me my expenses");

        Assert.Single(payload.Errors);
        Assert.Contains("membership not found", payload.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Null(payload.Data);
    }

    [Fact]
    public async Task Chat_Mutation_DoesNotLet_Manager_Read_Other_Department_Data()
    {
        await using var factory = new GraphQlApiTestFactory();

        var tenantId = Guid.NewGuid();
        var engineeringDepartmentId = Guid.NewGuid();
        var marketingDepartmentId = Guid.NewGuid();

        await factory.SeedTenantSubscriptionAsync(tenantId, PlanTier.Pro);

        var manager = await factory.CreateMembershipAsync(RoleType.Manager, tenantId, engineeringDepartmentId);
        var marketingStaff = await factory.CreateMembershipAsync(RoleType.Staff, tenantId, marketingDepartmentId);
        await factory.IndexReviewedExpenseAsync(marketingStaff, "Marketing Budget Review");

        using var client = factory.CreateClient();
        var result = await factory.ExecuteChatAsync(
            client,
            manager,
            "Show me Marketing Budget Review expense",
            departmentId: marketingDepartmentId);

        Assert.Single(result.Errors);
        Assert.Contains("outside your scope", result.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Chat_Mutation_DoesNotLet_TenantAdmin_Read_Another_Tenant_Data()
    {
        await using var factory = new GraphQlApiTestFactory();

        var sourceTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        await factory.SeedTenantSubscriptionAsync(sourceTenantId, PlanTier.Pro);
        await factory.SeedTenantSubscriptionAsync(otherTenantId, PlanTier.Pro);

        var tenantAdmin = await factory.CreateMembershipAsync(RoleType.TenantAdmin, sourceTenantId);
        var otherTenantStaff = await factory.CreateMembershipAsync(RoleType.Staff, otherTenantId, Guid.NewGuid());
        await factory.IndexReviewedExpenseAsync(otherTenantStaff, "Other Tenant Merchant");

        using var client = factory.CreateClient();
        var result = await factory.ExecuteChatAsync(client, tenantAdmin, "Show me Other Tenant Merchant expense");

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data!.DocumentCount);
        Assert.Contains("not enough authorized context", result.Data.Answer, StringComparison.OrdinalIgnoreCase);
    }
}
