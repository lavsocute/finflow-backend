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
    PromptBoundary,
    OwnSummary,
    OwnDetail,
    ApprovalQueue,
    Aggregate,
    Comparison,
    Ranking,
    DocumentLookup,
    DestructiveCommand,
    DestructiveAction,
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

public enum ChatReportingTask
{
    Unknown,
    Summary,
    Trend,
    VendorRanking,
    EmployeeRanking,
    BudgetUtilization,
    ApprovalQueue,
    Comparison,
    EntityStatusLookup
}

public sealed record ChatIntentClassification(
    ChatExecutionMode Mode,
    string Reason,
    ChatIntentFamily Family = ChatIntentFamily.Unknown,
    ChatScopeConfidence ScopeConfidence = ChatScopeConfidence.Ambiguous,
    ChatReportingTask ReportingTask = ChatReportingTask.Unknown);
