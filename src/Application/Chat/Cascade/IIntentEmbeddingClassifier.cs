using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Cascade;

public interface IIntentEmbeddingClassifier
{
    Task<IReadOnlyList<EmbeddingIntentMatch>> RankAsync(
        string query,
        int topK,
        CancellationToken ct);
}

public sealed record EmbeddingIntentMatch(
    string ExemplarId,
    string ExemplarText,
    ChatExecutionMode Mode,
    ChatIntentFamily Family,
    ChatReportingTask ReportingTask,
    double CosineSimilarity);
