using FinFlow.Domain.Chat;

namespace FinFlow.Application.Chat.Interfaces;

public interface ILlmIntentTracker
{
    Task<LlmIntentAnalysis> AnalyzeIntentAsync(
        string currentMessage,
        IReadOnlyList<ChatMessage> conversationHistory,
        CancellationToken ct = default);
}

public sealed record LlmIntentAnalysis(
    string IntentType,
    string Confidence,
    bool IsPivot,
    bool IsClarification,
    bool IsContinuation,
    string Reasoning);
