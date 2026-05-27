using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Interfaces;

public interface IConversationStateManager
{
    Task<ConversationContext> GetOrCreateContextAsync(Guid sessionId, CancellationToken ct = default);
    Task<ConversationContext?> GetContextAsync(Guid sessionId, CancellationToken ct = default);

    Task AddEntityAsync(Guid sessionId, TrackedEntity entity, CancellationToken ct = default);
    Task<TrackedEntity?> GetEntityAsync(Guid sessionId, string name, CancellationToken ct = default);
    Task<TrackedEntity?> GetEntityByTypeAsync(Guid sessionId, EntityType type, CancellationToken ct = default);
    Task RefreshEntityTtlAsync(Guid sessionId, Guid entityId, int ttlSeconds, CancellationToken ct = default);

    Task PushIntentAsync(Guid sessionId, string intentType, CancellationToken ct = default);
    Task<IntentFrame?> PopIntentAsync(Guid sessionId, CancellationToken ct = default);
    Task<IntentFrame?> PeekIntentAsync(Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<IntentFrame>> GetSuspendedIntentsAsync(Guid sessionId, CancellationToken ct = default);

    Task LockIntentAsync(Guid sessionId, string reason, CancellationToken ct = default);
    Task UnlockIntentAsync(Guid sessionId, CancellationToken ct = default);
    Task SuspendCurrentIntentAsync(Guid sessionId, string trigger, CancellationToken ct = default);
    Task ResumeSuspendedIntentAsync(Guid sessionId, CancellationToken ct = default);

    Task IncrementTurnAsync(Guid sessionId, CancellationToken ct = default);
    Task CleanupExpiredEntitiesAsync(Guid sessionId, CancellationToken ct = default);
    Task SaveContextAsync(Guid sessionId, ConversationContext context, CancellationToken ct = default);
    Task DeleteContextAsync(Guid sessionId, CancellationToken ct = default);
}

public static class ConversationStateCacheKeys
{
    private const string Prefix = "conv:ctx:";

    public static string Context(Guid sessionId) => $"{Prefix}{sessionId}";

    public static TimeSpan DefaultExpiration => TimeSpan.FromMinutes(30);
    public static TimeSpan ShortExpiration => TimeSpan.FromMinutes(5);
}