namespace FinFlow.IntegrationTests.Chat;

/// <summary>
/// Golden Q&A entry for chat regression testing.
/// </summary>
public sealed record ChatEvalEntry(
    string Id,
    string Query,
    string[] ExpectedKeywords,
    string[] ExpectedChunkTypes,
    string Role,
    string Notes);

/// <summary>
/// Aggregate report from running the eval suite.
/// </summary>
public sealed record ChatEvalReport(
    int TotalEntries,
    int Passed,
    int Failed,
    double KeywordCoveragePctAvg,
    double RetrievalRecallPctAvg,
    IReadOnlyList<ChatEvalItemResult> Items);

public sealed record ChatEvalItemResult(
    string Id,
    bool Passed,
    double KeywordCoveragePct,
    double RetrievalRecallPct,
    string? FailureReason);
