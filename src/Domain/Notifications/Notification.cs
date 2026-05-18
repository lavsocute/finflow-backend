using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Notifications;

/// <summary>
/// In-app notification delivered to a specific membership. Domain emits these
/// via <c>IDomainEventNotificationMapper</c> alongside audit logs in the
/// SaveChangesAsync pipeline.
///
/// Lifecycle:
///  - Created with <c>IsRead = false</c>
///  - Recipient marks as read → <see cref="MarkAsRead"/>
///  - No update other than read state (immutable payload)
/// </summary>
public sealed class Notification : Entity, IMultiTenant
{
    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 1000;
    private const int MaxTypeLength = 100;
    private const int MaxPayloadLength = 4000;

    private Notification(
        Guid id,
        Guid idTenant,
        Guid recipientMembershipId,
        string type,
        string title,
        string body,
        string payloadJson,
        NotificationSeverity severity,
        DateTime createdAt)
    {
        Id = id;
        IdTenant = idTenant;
        RecipientMembershipId = recipientMembershipId;
        Type = type;
        Title = title;
        Body = body;
        PayloadJson = payloadJson;
        Severity = severity;
        IsRead = false;
        ReadAt = null;
        CreatedAt = createdAt;
    }

    private Notification() { }

    public Guid IdTenant { get; private set; }
    public Guid RecipientMembershipId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public NotificationSeverity Severity { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime? ReadAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static Result<Notification> Create(
        Guid idTenant,
        Guid recipientMembershipId,
        string type,
        string title,
        string body,
        string? payloadJson,
        NotificationSeverity severity)
    {
        if (idTenant == Guid.Empty)
            return Result.Failure<Notification>(NotificationErrors.TenantRequired);
        if (recipientMembershipId == Guid.Empty)
            return Result.Failure<Notification>(NotificationErrors.RecipientRequired);
        if (string.IsNullOrWhiteSpace(type) || type.Length > MaxTypeLength)
            return Result.Failure<Notification>(NotificationErrors.TypeInvalid);
        if (string.IsNullOrWhiteSpace(title) || title.Length > MaxTitleLength)
            return Result.Failure<Notification>(NotificationErrors.TitleInvalid);
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyLength)
            return Result.Failure<Notification>(NotificationErrors.BodyInvalid);
        var safePayload = payloadJson ?? "{}";
        if (safePayload.Length > MaxPayloadLength)
            return Result.Failure<Notification>(NotificationErrors.PayloadTooLong);

        return Result.Success(new Notification(
            Guid.NewGuid(),
            idTenant,
            recipientMembershipId,
            type.Trim(),
            title.Trim(),
            body.Trim(),
            safePayload,
            severity,
            DateTime.UtcNow));
    }

    public Result MarkAsRead()
    {
        if (IsRead)
            return Result.Success();   // idempotent

        IsRead = true;
        ReadAt = DateTime.UtcNow;
        return Result.Success();
    }
}

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public static class NotificationErrors
{
    public static readonly Error TenantRequired = new("Notification.TenantRequired", "Tenant is required.");
    public static readonly Error RecipientRequired = new("Notification.RecipientRequired", "Recipient membership is required.");
    public static readonly Error TypeInvalid = new("Notification.TypeInvalid", "Type must be a non-empty string up to 100 chars.");
    public static readonly Error TitleInvalid = new("Notification.TitleInvalid", "Title must be a non-empty string up to 200 chars.");
    public static readonly Error BodyInvalid = new("Notification.BodyInvalid", "Body must be a non-empty string up to 1000 chars.");
    public static readonly Error PayloadTooLong = new("Notification.PayloadTooLong", "Payload exceeds 4000 chars.");
    public static readonly Error NotFound = new("Notification.NotFound", "Notification not found.");
    public static readonly Error NotForRecipient = new("Notification.NotForRecipient", "Notification does not belong to the current user.");
}
