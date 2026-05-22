namespace FinFlow.Application.Chat.Interfaces;

public interface IChatReportingService
{
    Task<ChatReportingAnswer> BuildOwnExpenseSummaryAsync(
        ChatAuthorizationProfile profile,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<ChatReportingAnswer> BuildScopedExpenseSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<ChatReportingAnswer> BuildTopEmployeesSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<ChatReportingAnswer> BuildMonthlyTrendSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        CancellationToken cancellationToken = default);

    Task<ChatReportingAnswer> BuildTopVendorsSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<ChatReportingAnswer> BuildBudgetUtilizationSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<ChatReportingAnswer> BuildPendingApprovalSummaryAsync(
        ChatAuthorizationProfile profile,
        string query,
        CancellationToken cancellationToken = default);

    Task<ChatReportingAnswer> BuildExpenseComparisonAsync(
        ChatAuthorizationProfile profile,
        string query,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

public sealed record ChatReportingAnswer(
    string Answer,
    string SourceKind,
    int RecordCount);
