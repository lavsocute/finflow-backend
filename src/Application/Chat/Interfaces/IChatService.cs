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
    string? CompleteAnswer = null);

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

public record ChatResponse(
    string Answer,
    Guid SessionId,
    Guid MessageId,
    int DocumentCount,
    int TokenUsage,
    IReadOnlyList<ChatCitation>? Citations = null
);

/// <summary>
/// Reference from a chat answer back to the chunk that supported it.
/// </summary>
public sealed record ChatCitation(
    int ChunkNumber,
    Guid ChunkId,
    Guid DocumentId,
    string ChunkType,
    string Preview);