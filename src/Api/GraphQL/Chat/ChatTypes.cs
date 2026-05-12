namespace FinFlow.Api.GraphQL.Chat;

public sealed class ChatMessageType
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public Guid SenderId { get; init; }
    public string Role { get; init; } = "";
    public string Content { get; init; } = "";
    public int? TokenCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class ChatSessionSummaryType
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public int MessageCount { get; init; }
    public DateTime? LastMessageAt { get; init; }
}