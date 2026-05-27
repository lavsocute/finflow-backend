using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Manages conversation state including entities, intent stack, and turn tracking with cache persistence.
/// </summary>
public sealed class ConversationStateManager : IConversationStateManager
{
    private readonly ICacheService _cache;
    private readonly ILogger<ConversationStateManager> _logger;

    public ConversationStateManager(
        ICacheService cache,
        ILogger<ConversationStateManager> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<ConversationContext> GetOrCreateContextAsync(Guid sessionId, CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<ConversationContextSnapshot>(
            ConversationStateCacheKeys.Context(sessionId), ct);

        if (cached != null)
        {
            _logger.LogDebug("Retrieved existing context for session {SessionId}", sessionId);
            return cached.ToDomain();
        }

        var context = ConversationContext.Create(sessionId);
        await _cache.SetAsync(
            ConversationStateCacheKeys.Context(sessionId),
            ConversationContextSnapshot.FromDomain(context),
            ConversationStateCacheKeys.DefaultExpiration,
            ct);

        _logger.LogDebug("Created new context for session {SessionId}", sessionId);
        return context;
    }

    public async Task<ConversationContext?> GetContextAsync(Guid sessionId, CancellationToken ct = default)
    {
        var snapshot = await _cache.GetAsync<ConversationContextSnapshot>(
            ConversationStateCacheKeys.Context(sessionId), ct);
        return snapshot?.ToDomain();
    }

    public async Task AddEntityAsync(Guid sessionId, TrackedEntity entity, CancellationToken ct = default)
    {
        var context = await GetOrCreateContextAsync(sessionId, ct);
        context.AddEntity(entity);
        await SaveContextAsync(sessionId, context, ct);
        _logger.LogDebug("Added entity {EntityId} to session {SessionId}", entity.Id, sessionId);
    }

    public async Task<TrackedEntity?> GetEntityAsync(Guid sessionId, string name, CancellationToken ct = default)
    {
        var context = await GetContextAsync(sessionId, ct);
        return context?.FindEntity(name);
    }

    public async Task<TrackedEntity?> GetEntityByTypeAsync(Guid sessionId, EntityType type, CancellationToken ct = default)
    {
        var context = await GetContextAsync(sessionId, ct);
        return context?.FindEntityByType(type);
    }

    public async Task RefreshEntityTtlAsync(Guid sessionId, Guid entityId, int ttlSeconds, CancellationToken ct = default)
    {
        var context = await GetContextAsync(sessionId, ct);
        if (context == null) return;

        var entity = context.Entities.FirstOrDefault(e => e.Id == entityId);
        entity?.RefreshTtl(ttlSeconds);
        await SaveContextAsync(sessionId, context, ct);
    }

    public async Task PushIntentAsync(Guid sessionId, string intentType, CancellationToken ct = default)
    {
        var context = await GetOrCreateContextAsync(sessionId, ct);

        if (context.IntentStack.IsLocked)
        {
            _logger.LogWarning(
                "Cannot push intent {IntentType} to locked stack for session {SessionId}. Reason: {Reason}",
                intentType, sessionId, context.IntentStack.LockReason);
            return;
        }

        var frame = IntentFrame.Create(intentType);
        context.IntentStack.Push(frame);
        await SaveContextAsync(sessionId, context, ct);

        _logger.LogDebug("Pushed intent {IntentType} for session {SessionId}", intentType, sessionId);
    }

    public async Task<IntentFrame?> PopIntentAsync(Guid sessionId, CancellationToken ct = default)
    {
        var context = await GetOrCreateContextAsync(sessionId, ct);
        var popped = context.IntentStack.Pop();
        await SaveContextAsync(sessionId, context, ct);

        if (popped != null)
            _logger.LogDebug("Popped intent {IntentType} for session {SessionId}", popped.IntentType, sessionId);

        return popped;
    }

    public async Task<IntentFrame?> PeekIntentAsync(Guid sessionId, CancellationToken ct = default)
    {
        var context = await GetContextAsync(sessionId, ct);
        return context?.IntentStack.GetActive();
    }

    public async Task<IReadOnlyList<IntentFrame>> GetSuspendedIntentsAsync(Guid sessionId, CancellationToken ct = default)
    {
        var context = await GetContextAsync(sessionId, ct);
        return context?.IntentStack.GetSuspended() ?? [];
    }

    public async Task LockIntentAsync(Guid sessionId, string reason, CancellationToken ct = default)
    {
        var context = await GetOrCreateContextAsync(sessionId, ct);
        context.IntentStack.Lock(reason);
        await SaveContextAsync(sessionId, context, ct);

        _logger.LogDebug("Locked intent stack for session {SessionId}. Reason: {Reason}", sessionId, reason);
    }

    public async Task UnlockIntentAsync(Guid sessionId, CancellationToken ct = default)
    {
        var context = await GetOrCreateContextAsync(sessionId, ct);
        context.IntentStack.Unlock();
        await SaveContextAsync(sessionId, context, ct);

        _logger.LogDebug("Unlocked intent stack for session {SessionId}", sessionId);
    }

    public async Task SuspendCurrentIntentAsync(Guid sessionId, string trigger, CancellationToken ct = default)
    {
        var context = await GetOrCreateContextAsync(sessionId, ct);
        context.IntentStack.SuspendCurrent(trigger);
        await SaveContextAsync(sessionId, context, ct);

        _logger.LogDebug("Suspended current intent for session {SessionId}. Trigger: {Trigger}", sessionId, trigger);
    }

    public async Task ResumeSuspendedIntentAsync(Guid sessionId, CancellationToken ct = default)
    {
        var context = await GetOrCreateContextAsync(sessionId, ct);
        var resumed = context.IntentStack.Resume();
        await SaveContextAsync(sessionId, context, ct);

        if (resumed != null)
            _logger.LogDebug("Resumed intent {IntentType} for session {SessionId}", resumed.IntentType, sessionId);
    }

    public async Task IncrementTurnAsync(Guid sessionId, CancellationToken ct = default)
    {
        var context = await GetOrCreateContextAsync(sessionId, ct);
        context.IncrementTurn();
        await SaveContextAsync(sessionId, context, ct);
    }

    public async Task CleanupExpiredEntitiesAsync(Guid sessionId, CancellationToken ct = default)
    {
        var context = await GetContextAsync(sessionId, ct);
        if (context == null) return;

        context.CleanupExpiredEntities();
        await SaveContextAsync(sessionId, context, ct);

        _logger.LogDebug("Cleaned up expired entities for session {SessionId}", sessionId);
    }

    public async Task DeleteContextAsync(Guid sessionId, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(ConversationStateCacheKeys.Context(sessionId), ct);
        _logger.LogDebug("Deleted context for session {SessionId}", sessionId);
    }

    public async Task SaveContextAsync(Guid sessionId, ConversationContext context, CancellationToken ct)
    {
        await _cache.SetAsync(
            ConversationStateCacheKeys.Context(sessionId),
            ConversationContextSnapshot.FromDomain(context),
            ConversationStateCacheKeys.DefaultExpiration,
            ct);
    }
}

internal sealed record ConversationContextSnapshot(
    Guid SessionId,
    int TurnCount,
    string? CompressedSummary,
    PendingClarificationSnapshot? PendingClarification,
    ConversationTurnStateSnapshot? LastTurn,
    IReadOnlyList<TrackedEntitySnapshot> Entities,
    IntentStackSnapshot IntentStack)
{
    public static ConversationContextSnapshot FromDomain(ConversationContext context) =>
        new(
            context.SessionId,
            context.TurnCount,
            context.CompressedSummary,
            context.PendingClarification is null ? null : PendingClarificationSnapshot.FromDomain(context.PendingClarification),
            context.LastTurn is null ? null : ConversationTurnStateSnapshot.FromDomain(context.LastTurn),
            context.Entities.Select(TrackedEntitySnapshot.FromDomain).ToList(),
            IntentStackSnapshot.FromDomain(context.IntentStack));

    public ConversationContext ToDomain()
    {
        var context = ConversationContext.Create(SessionId);

        for (var i = 0; i < TurnCount; i++)
            context.IncrementTurn();

        if (!string.IsNullOrWhiteSpace(CompressedSummary))
            context.SetCompressedSummary(CompressedSummary);

        foreach (var entity in Entities)
            context.AddEntity(entity.ToDomain());

        if (PendingClarification is not null)
            context.SetPendingClarification(PendingClarification.ToDomain());

        if (LastTurn is not null)
            context.SetLastTurn(LastTurn.ToDomain());

        IntentStack.ApplyTo(context.IntentStack);

        return context;
    }
}

internal sealed record ConversationTurnStateSnapshot(
    string OriginalQuery,
    string EffectiveQuery,
    string ExecutionMode,
    string IntentFamily,
    string ReportingTask,
    string IntentReason,
    string ScopeConfidence,
    string AnswerSource,
    DateOnly? PeriodFrom,
    DateOnly? PeriodTo)
{
    public static ConversationTurnStateSnapshot FromDomain(ConversationTurnState turn) =>
        new(
            turn.OriginalQuery,
            turn.EffectiveQuery,
            turn.ExecutionMode,
            turn.IntentFamily,
            turn.ReportingTask,
            turn.IntentReason,
            turn.ScopeConfidence,
            turn.AnswerSource,
            turn.PeriodFrom,
            turn.PeriodTo);

    public ConversationTurnState ToDomain() =>
        ConversationTurnState.Create(
            OriginalQuery,
            EffectiveQuery,
            ExecutionMode,
            IntentFamily,
            ReportingTask,
            IntentReason,
            ScopeConfidence,
            AnswerSource,
            PeriodFrom,
            PeriodTo);
}

internal sealed record PendingClarificationSnapshot(
    PendingClarificationKind Kind,
    string OriginalQuery,
    string Prompt,
    string? IntentReason)
{
    public static PendingClarificationSnapshot FromDomain(PendingClarification clarification) =>
        new(clarification.Kind, clarification.OriginalQuery, clarification.Prompt, clarification.IntentReason);

    public PendingClarification ToDomain() =>
        Kind == PendingClarificationKind.Scope
            ? PendingClarification.Scope(OriginalQuery, Prompt, IntentReason)
            : PendingClarification.Scope(OriginalQuery, Prompt, IntentReason);
}

internal sealed record TrackedEntitySnapshot(
    string CanonicalName,
    EntityType Type,
    IReadOnlyList<string> Aliases,
    Dictionary<string, object> Attributes,
    int FirstMentionTurn,
    int TtlSeconds)
{
    public static TrackedEntitySnapshot FromDomain(TrackedEntity entity) =>
        new(
            entity.CanonicalName,
            entity.Type,
            entity.Aliases.ToList(),
            new Dictionary<string, object>(entity.Attributes),
            entity.FirstMentionTurn,
            entity.TtlSeconds);

    public TrackedEntity ToDomain() =>
        TrackedEntity.ExtractEntity(
            CanonicalName,
            Type,
            FirstMentionTurn,
            Aliases.ToList(),
            Attributes,
            TtlSeconds);
}

internal sealed record IntentStackSnapshot(
    bool IsLocked,
    string? LockReason,
    IReadOnlyList<IntentFrameSnapshot> Frames)
{
    public static IntentStackSnapshot FromDomain(IntentStack stack) =>
        new(
            stack.IsLocked,
            stack.LockReason,
            stack.Frames.Select(IntentFrameSnapshot.FromDomain).ToList());

    public void ApplyTo(IntentStack stack)
    {
        foreach (var frame in Frames)
            stack.Push(frame.ToDomain());

        if (IsLocked && !string.IsNullOrWhiteSpace(LockReason))
            stack.Lock(LockReason);
    }
}

internal sealed record IntentFrameSnapshot(
    string RawIntentType,
    IntentState State,
    string? PivotTrigger,
    bool IsPivot,
    bool IsClarification,
    bool IsContinuation,
    string? Confidence,
    string? Reasoning,
    Dictionary<string, object?> Slots)
{
    public static IntentFrameSnapshot FromDomain(IntentFrame frame) =>
        new(
            frame.RawIntentType,
            frame.State,
            frame.PivotTrigger,
            frame.IsPivot,
            frame.IsClarification,
            frame.IsContinuation,
            frame.Confidence,
            frame.Reasoning,
            new Dictionary<string, object?>(frame.Slots));

    public IntentFrame ToDomain()
    {
        var frame = IntentFrame.CreateFromLlm(
            RawIntentType,
            IsPivot,
            IsClarification,
            IsContinuation,
            Confidence,
            Reasoning);

        foreach (var slot in Slots)
            frame.SetSlot(slot.Key, slot.Value);

        if (State == IntentState.Suspended && !string.IsNullOrWhiteSpace(PivotTrigger))
            frame.Suspend(PivotTrigger);
        else
            frame.SetState(State);

        return frame;
    }
}
