using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Domain.Chat;

public class ChatSession : Entity, IMultiTenant, ISoftDeletable
{
    public Guid IdTenant { get; private set; }
    public Guid MembershipId { get; private set; }
    public string Title { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public bool IsActive { get; private set; }

    private ChatSession() { }

    public static ChatSession Create(Guid tenantId, Guid membershipId, string title)
    {
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            IdTenant = tenantId,
            MembershipId = membershipId,
            Title = string.IsNullOrWhiteSpace(title) ? $"Chat {DateTime.UtcNow:yyyy-MM-dd HH:mm}" : title,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };
        return session;
    }

    public void UpdateTitle(string title)
    {
        Title = title;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkInactive()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }
}