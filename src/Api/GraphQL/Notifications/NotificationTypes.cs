using FinFlow.Domain.Notifications;

namespace FinFlow.Api.GraphQL.Notifications;

public sealed class NotificationPayload
{
    public Guid Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public bool IsRead { get; init; }
    public DateTime? ReadAt { get; init; }
    public DateTime CreatedAt { get; init; }

    public static NotificationPayload From(NotificationSummary dto) => new()
    {
        Id = dto.Id,
        Type = dto.Type,
        Title = dto.Title,
        Body = dto.Body,
        PayloadJson = dto.PayloadJson,
        Severity = dto.Severity.ToString(),
        IsRead = dto.IsRead,
        ReadAt = dto.ReadAt,
        CreatedAt = dto.CreatedAt,
    };
}
