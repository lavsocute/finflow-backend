using FinFlow.Domain.Abstractions;

namespace FinFlow.Domain.Chat;

public class ChatMessage : Entity
{
    public Guid SessionId { get; private set; }
    public Guid SenderId { get; private set; }
    public ChatMessageRole Role { get; private set; }
    public string Content { get; private set; }
    public int? TokenCount { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ChatMessage() { }

    public static ChatMessage Create(
        Guid sessionId,
        Guid senderId,
        ChatMessageRole role,
        string content,
        int? tokenCount = null)
    {
        return new ChatMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            SenderId = senderId,
            Role = role,
            Content = content,
            TokenCount = tokenCount,
            CreatedAt = DateTime.UtcNow
        };
    }
}