using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.Application.Chat.Services;

/// <summary>
/// Calculates weighted confidence scores for context resolution across intent, entity, context, history, and domain factors.
/// </summary>
public sealed class ConfidenceScorer : IConfidenceScorer
{
    private readonly float _intentWeight;
    private readonly float _entityWeight;
    private readonly float _contextWeight;
    private readonly float _historyWeight;
    private readonly float _domainWeight;
    private readonly ContextResolutionMetrics? _metrics;

    public float IntentWeight => _intentWeight;
    public float EntityWeight => _entityWeight;
    public float ContextWeight => _contextWeight;
    public float HistoryWeight => _historyWeight;
    public float DomainWeight => _domainWeight;

    public ConfidenceScorer(
        float intentWeight = 0.35f,
        float entityWeight = 0.25f,
        float contextWeight = 0.20f,
        float historyWeight = 0.10f,
        float domainWeight = 0.10f,
        ContextResolutionMetrics? metrics = null)
    {
        _intentWeight = intentWeight;
        _entityWeight = entityWeight;
        _contextWeight = contextWeight;
        _historyWeight = historyWeight;
        _domainWeight = domainWeight;
        _metrics = metrics;
    }

    public ConfidenceScore CalculateScore(
        float intentScore,
        float entityScore,
        float contextScore,
        float historyScore,
        float domainScore)
    {
        var factors = new Dictionary<string, float>
        {
            ["intent"] = intentScore,
            ["entity"] = entityScore,
            ["context"] = contextScore,
            ["history"] = historyScore,
            ["domain"] = domainScore
        };

        var total = (intentScore * _intentWeight) +
                   (entityScore * _entityWeight) +
                   (contextScore * _contextWeight) +
                   (historyScore * _historyWeight) +
                   (domainScore * _domainWeight);

        total = Math.Clamp(total, 0f, 1f);

        var score = new ConfidenceScore
        {
            Total = total,
            Level = GetLevel(total),
            Factors = factors
        };

        _metrics?.RecordConfidenceScore(total, score);

        return score;
    }

    public ConfidenceLevel GetLevel(float confidence)
    {
        return confidence switch
        {
            >= 0.85f => ConfidenceLevel.High,
            >= 0.60f => ConfidenceLevel.Medium,
            _ => ConfidenceLevel.Low
        };
    }

    public string GetAction(ConfidenceLevel level)
    {
        return level switch
        {
            ConfidenceLevel.High => "EXECUTE",
            ConfidenceLevel.Medium => "EXECUTE_WITH_LOG",
            ConfidenceLevel.Low => "CLARIFY"
        };
    }
}