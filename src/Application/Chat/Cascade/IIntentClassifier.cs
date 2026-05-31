using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Cascade;

public interface IIntentClassifier
{
    Task<IntentClassificationResult> ClassifyAsync(IntentClassificationContext context, CancellationToken ct = default);
}

public sealed record IntentClassificationContext(
    string Query,
    string NormalizedQuery,
    DateOnly Today,
    string? ActorRole = null,
    Guid? TenantId = null);

public sealed record IntentClassificationResult(
    ChatExecutionMode Mode,
    ChatIntentFamily Family,
    ChatReportingTask ReportingTask,
    ChatScopeConfidence ScopeConfidence,
    string Reason,
    double Confidence,
    string ClassifierStage,
    string? ModelInvoked = null,
    int LatencyMs = 0)
{
    public ChatIntentClassification ToClassification() =>
        new(Mode, Reason, Family, ScopeConfidence, ReportingTask);

    public static IntentClassificationResult Abstain(string reason) =>
        new(ChatExecutionMode.Rag, ChatIntentFamily.Unknown, ChatReportingTask.Unknown,
            ChatScopeConfidence.Ambiguous, reason, 0.0, ClassifierStages.Abstain);
}

public static class ClassifierStages
{
    public const string Safety = "safety";
    public const string Embedding = "embedding";
    public const string Llm = "llm";
    public const string DefaultRag = "default-rag";
    public const string Abstain = "abstain";
}
