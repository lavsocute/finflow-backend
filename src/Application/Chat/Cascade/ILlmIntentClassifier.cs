using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Cascade;

public interface ILlmIntentClassifier
{
    Task<LlmIntentClassificationResult> ClassifyAsync(
        IntentClassificationContext context,
        EmbeddingIntentMatch? topHint,
        CancellationToken ct);
}

public sealed record LlmIntentClassificationResult(
    ChatExecutionMode Mode,
    ChatIntentFamily Family,
    ChatReportingTask ReportingTask,
    ChatScopeConfidence ScopeConfidence,
    string Reason,
    double Confidence,
    string ModelInvoked,
    int LatencyMs,
    bool IsFallback);
