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

    public string? IntentFamily { get; private set; }
    public string? AnswerSource { get; private set; }
    public string? RetrievedChunkIds { get; private set; }
    public Guid? EffectiveDepartmentId { get; private set; }
    public string? ScopeContext { get; private set; }
    public string? ContextVersion { get; private set; }

    private ChatMessage() { }

    public static ChatMessage Create(
        Guid sessionId,
        Guid senderId,
        ChatMessageRole role,
        string content,
        int? tokenCount = null,
        DateTime? createdAtUtc = null)
    {
        return new ChatMessage
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            SenderId = senderId,
            Role = role,
            Content = content,
            TokenCount = tokenCount,
            CreatedAt = createdAtUtc ?? DateTime.UtcNow
        };
    }

    public void SetIntentFamily(string intentFamily)
    {
        IntentFamily = intentFamily;
    }

    public void SetAnswerSource(string answerSource)
    {
        AnswerSource = answerSource;
    }

    public void SetRetrievedChunkIds(string chunkIds)
    {
        RetrievedChunkIds = chunkIds;
    }

    public void SetEffectiveDepartmentId(Guid? departmentId)
    {
        EffectiveDepartmentId = departmentId;
    }

    public void SetScopeContext(string scopeContext)
    {
        ScopeContext = scopeContext;
    }

    public void SetContextVersion(string version)
    {
        ContextVersion = version;
    }
}
