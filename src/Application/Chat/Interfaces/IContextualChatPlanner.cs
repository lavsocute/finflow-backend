using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Interfaces;

public interface IContextualChatPlanner
{
    Task<ContextualChatPlan?> PlanAsync(
        ContextualChatPlanRequest request,
        CancellationToken ct = default);
}

public sealed record ContextualChatPlanRequest(
    string Query,
    string EffectiveQuery,
    ChatIntentClassification InitialIntent,
    IReadOnlyList<ChatMessage> History,
    ConversationTurnState? LastTurn,
    DateOnly Today);

public sealed record ContextualChatPlan(
    string EffectiveQuery,
    ChatIntentClassification Intent,
    DateOnly? ReportingFrom,
    DateOnly? ReportingTo);
