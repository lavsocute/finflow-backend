using FinFlow.Domain.Chat;
using FinFlow.Domain.Interfaces;

namespace FinFlow.Application.Chat.Interfaces;

public interface IChatService
{
    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ChatStreamEvent> ChatStreamAsync(ChatRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(Guid sessionId, Guid membershipId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSessionSummary>> GetSessionsAsync(Guid membershipId, int limit = 20, CancellationToken ct = default);
}

public enum ChatAnswerSource
{
    General,
    Reporting,
    Rag
}

/// <summary>
/// Stream event from <see cref="IChatService.ChatStreamAsync"/>.
/// Multiple <see cref="ChatStreamEventKind.Token"/> events emit during generation,
/// then exactly one <see cref="ChatStreamEventKind.Complete"/> closes the stream.
/// </summary>
public sealed record ChatStreamEvent(
    ChatStreamEventKind Kind,
    string? TokenChunk = null,
    Guid? SessionId = null,
    Guid? MessageId = null,
    int? DocumentCount = null,
    int? TokenUsage = null,
    string? CompleteAnswer = null,
    ChatAnswerSource? AnswerSource = null);

public enum ChatStreamEventKind
{
    Token,
    Complete,
    Error
}

public record ChatRequest(
    Guid MembershipId,
    Guid TenantId,
    Guid? SessionId,
    string Query,
    Guid? DepartmentId
);

public record ChatResponse
{
    public ChatResponse(
        string Answer,
        Guid SessionId,
        Guid MessageId,
        int DocumentCount,
        int TokenUsage,
        IReadOnlyList<ChatCitation>? Citations = null)
        : this(Answer, SessionId, MessageId, DocumentCount, TokenUsage, ChatAnswerSource.Rag, Citations)
    {
    }

    public ChatResponse(
        string Answer,
        Guid SessionId,
        Guid MessageId,
        int DocumentCount,
        int TokenUsage,
        ChatAnswerSource AnswerSource,
        IReadOnlyList<ChatCitation>? Citations = null)
    {
        this.Answer = Answer;
        this.SessionId = SessionId;
        this.MessageId = MessageId;
        this.DocumentCount = DocumentCount;
        this.TokenUsage = TokenUsage;
        this.AnswerSource = AnswerSource;
        this.Citations = Citations;
    }

    public string Answer { get; init; }
    public Guid SessionId { get; init; }
    public Guid MessageId { get; init; }
    public int DocumentCount { get; init; }
    public int TokenUsage { get; init; }
    public ChatAnswerSource AnswerSource { get; init; }
    public IReadOnlyList<ChatCitation>? Citations { get; init; }
}

/// <summary>
/// Reference from a chat answer back to the chunk that supported it.
/// </summary>
public sealed record ChatCitation(
    int ChunkNumber,
    Guid ChunkId,
    Guid DocumentId,
    string ChunkType,
    string Preview);
