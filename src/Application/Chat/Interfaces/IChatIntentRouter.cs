namespace FinFlow.Application.Chat.Interfaces;

public interface IChatIntentRouter
{
    ChatIntentClassification Classify(string query);
}

public enum ChatIntentFamily
{
    Greeting,
    SmallTalk,
    Productivity,
    LowSignal,
    Programming,
    SensitiveAdvice,
    OwnSummary,
    OwnDetail,
    ApprovalQueue,
    Aggregate,
    Comparison,
    Ranking,
    DocumentLookup,
    Unknown
}

public enum ChatScopeConfidence
{
    Explicit,
    SafeInferred,
    Ambiguous,
    Forbidden
}

public enum ChatExecutionMode
{
    Greeting,
    General,
    Reporting,
    Rag
}

public sealed record ChatIntentClassification(
    ChatExecutionMode Mode,
    string Reason,
    ChatIntentFamily Family = ChatIntentFamily.Unknown,
    ChatScopeConfidence ScopeConfidence = ChatScopeConfidence.Ambiguous);
