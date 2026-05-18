using FinFlow.Domain.Notifications;
using Xunit;

namespace FinFlow.UnitTests.Domain.Notifications;

public class NotificationTests
{
    [Fact]
    public void Create_HappyPath_ReturnsUnreadNotification()
    {
        var result = Notification.Create(
            idTenant: Guid.NewGuid(),
            recipientMembershipId: Guid.NewGuid(),
            type: "PAYMENT_CONFIRMED",
            title: "Đã được hoàn tiền",
            body: "Hóa đơn INV-001 đã được hoàn 1.000.000 VND.",
            payloadJson: "{\"paymentId\":\"...\"}",
            severity: NotificationSeverity.Info);

        Assert.True(result.IsSuccess);
        var n = result.Value;
        Assert.False(n.IsRead);
        Assert.Null(n.ReadAt);
        Assert.NotEqual(Guid.Empty, n.Id);
        Assert.Equal(NotificationSeverity.Info, n.Severity);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Create_RequiresTitle(string? title)
    {
        var result = Notification.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            "TYPE", title!, "body", null, NotificationSeverity.Info);
        Assert.True(result.IsFailure);
        Assert.Equal(NotificationErrors.TitleInvalid, result.Error);
    }

    [Fact]
    public void Create_EmptyTenant_Rejected()
    {
        var result = Notification.Create(
            Guid.Empty, Guid.NewGuid(),
            "TYPE", "title", "body", null, NotificationSeverity.Info);
        Assert.True(result.IsFailure);
        Assert.Equal(NotificationErrors.TenantRequired, result.Error);
    }

    [Fact]
    public void MarkAsRead_SetsFlagAndTimestamp()
    {
        var n = Notification.Create(Guid.NewGuid(), Guid.NewGuid(), "T", "title", "body", null, NotificationSeverity.Info).Value;

        var result = n.MarkAsRead();

        Assert.True(result.IsSuccess);
        Assert.True(n.IsRead);
        Assert.NotNull(n.ReadAt);
    }

    [Fact]
    public void MarkAsRead_AlreadyRead_IsIdempotent()
    {
        var n = Notification.Create(Guid.NewGuid(), Guid.NewGuid(), "T", "title", "body", null, NotificationSeverity.Info).Value;
        n.MarkAsRead();
        var firstReadAt = n.ReadAt;

        Thread.Sleep(2);   // ensure timestamp would change if not idempotent
        var second = n.MarkAsRead();

        Assert.True(second.IsSuccess);
        Assert.Equal(firstReadAt, n.ReadAt);   // unchanged
    }

    [Fact]
    public void Create_PayloadOver4000Chars_Rejected()
    {
        var bigPayload = "{\"x\":\"" + new string('a', 4100) + "\"}";
        var result = Notification.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            "T", "title", "body", bigPayload, NotificationSeverity.Info);
        Assert.True(result.IsFailure);
        Assert.Equal(NotificationErrors.PayloadTooLong, result.Error);
    }
}
