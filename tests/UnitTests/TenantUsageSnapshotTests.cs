using FinFlow.Domain.Entities;
using Xunit;

namespace FinFlow.UnitTests;

public sealed class TenantUsageSnapshotTests
{
    [Fact]
    public void Create_SetsInitialCountersToZero()
    {
        var tenantId = Guid.NewGuid();
        var periodStart = new DateOnly(2026, 4, 1);
        var periodEnd = new DateOnly(2026, 4, 30);

        var result = TenantUsageSnapshot.Create(tenantId, periodStart, periodEnd);

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, result.Value.IdTenant);
        Assert.Equal(periodStart, result.Value.PeriodStart);
        Assert.Equal(periodEnd, result.Value.PeriodEnd);
        Assert.Equal(0, result.Value.OcrPagesUsed);
        Assert.Equal(0, result.Value.ChatbotMessagesUsed);
        Assert.Equal(0L, result.Value.StorageUsedBytes);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public void Create_Fails_WhenTenantIdIsEmpty()
    {
        var result = TenantUsageSnapshot.Create(Guid.Empty, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        Assert.True(result.IsFailure);
        Assert.Equal(TenantUsageSnapshotErrors.TenantRequired.Code, result.Error.Code);
    }

    [Fact]
    public void Create_Fails_WhenPeriodIsInvalid()
    {
        var tenantId = Guid.NewGuid();

        var result = TenantUsageSnapshot.Create(tenantId, new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 30));

        Assert.True(result.IsFailure);
        Assert.Equal(TenantUsageSnapshotErrors.InvalidPeriod.Code, result.Error.Code);
    }

    [Fact]
    public void RecordOcrUsage_IncrementsPages()
    {
        var snapshot = TenantUsageSnapshot.Create(Guid.NewGuid(), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)).Value;

        var result = snapshot.RecordOcrUsage(3);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, snapshot.OcrPagesUsed);
    }

    [Fact]
    public void RecordChatbotUsage_IncrementsMessages()
    {
        var snapshot = TenantUsageSnapshot.Create(Guid.NewGuid(), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)).Value;

        var result = snapshot.RecordChatbotUsage(2);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, snapshot.ChatbotMessagesUsed);
    }

    [Fact]
    public void SetStorageUsedBytes_RejectsNegativeValues()
    {
        var snapshot = TenantUsageSnapshot.Create(Guid.NewGuid(), new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)).Value;

        var result = snapshot.SetStorageUsedBytes(-1);

        Assert.True(result.IsFailure);
        Assert.Equal(TenantUsageSnapshotErrors.StorageUsageCannotBeNegative.Code, result.Error.Code);
    }
}
