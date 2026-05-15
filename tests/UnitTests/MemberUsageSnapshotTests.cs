using FinFlow.Domain.Entities;
using Xunit;

namespace FinFlow.UnitTests;

public sealed class MemberUsageSnapshotTests
{
    [Fact]
    public void Create_Succeeds_WithValidTenantMembershipAndPeriod()
    {
        var tenantId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var periodStart = new DateOnly(2026, 5, 1);
        var periodEnd = new DateOnly(2026, 5, 31);

        var result = MemberUsageSnapshot.Create(tenantId, membershipId, periodStart, periodEnd);

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, result.Value.IdTenant);
        Assert.Equal(membershipId, result.Value.MembershipId);
        Assert.Equal(periodStart, result.Value.PeriodStart);
        Assert.Equal(periodEnd, result.Value.PeriodEnd);
        Assert.Equal(0, result.Value.ChatbotMessagesUsed);
        Assert.Equal(0, result.Value.OcrPagesUsed);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public void Create_Fails_WhenMembershipIdIsEmpty()
    {
        var result = MemberUsageSnapshot.Create(
            Guid.NewGuid(),
            Guid.Empty,
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31));

        Assert.True(result.IsFailure);
        Assert.Equal(MemberUsageSnapshotErrors.MembershipRequired.Code, result.Error.Code);
    }

    [Fact]
    public void RecordChatbotUsage_Fails_WhenMessageCountIsNotPositive()
    {
        var snapshot = MemberUsageSnapshot.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31)).Value;

        var result = snapshot.RecordChatbotUsage(0);

        Assert.True(result.IsFailure);
        Assert.Equal(MemberUsageSnapshotErrors.ChatbotUsageMustBePositive.Code, result.Error.Code);
    }

    [Fact]
    public void RecordOcrUsage_IncrementsPages()
    {
        var snapshot = MemberUsageSnapshot.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31)).Value;

        var result = snapshot.RecordOcrUsage(3);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, snapshot.OcrPagesUsed);
    }
}
