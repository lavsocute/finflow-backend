using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Chat;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Chat.Services;

public sealed class ContextResolutionMetrics
{
    private readonly ILogger<ContextResolutionMetrics> _logger;

    // Counters for resolution tiers
    private long _patternResolutions;
    private long _cacheResolutions;
    private long _smallLlmResolutions;
    private long _largeLlmResolutions;

    // Success counters per tier
    private long _patternSuccesses;
    private long _cacheSuccesses;
    private long _smallLlmSuccesses;
    private long _largeLlmSuccesses;

    // Clarification tracking
    private long _clarificationRequests;
    private long _totalResolutions;

    // Entity extraction tracking
    private long _entityExtractionAttempts;
    private long _entityExtractionSuccesses;

    // Confidence score accumulators
    private double _totalConfidenceSum;
    private double _totalIntentScoreSum;
    private double _totalEntityScoreSum;
    private double _totalContextScoreSum;
    private double _totalHistoryScoreSum;
    private double _totalDomainScoreSum;
    private long _confidenceScoringCount;

    // Intent stack depth distribution (bucket by depth 0-20+)
    private readonly long[] _intentStackDepthBuckets = new long[21];

    public ContextResolutionMetrics(ILogger<ContextResolutionMetrics> logger)
    {
        _logger = logger;
    }

    public void RecordResolutionAttempt(ResolutionTier tier)
    {
        Interlocked.Increment(ref _totalResolutions);

        switch (tier)
        {
            case ResolutionTier.Pattern:
                Interlocked.Increment(ref _patternResolutions);
                break;
            case ResolutionTier.Cache:
                Interlocked.Increment(ref _cacheResolutions);
                break;
            case ResolutionTier.SmallLlm:
                Interlocked.Increment(ref _smallLlmResolutions);
                break;
            case ResolutionTier.LargeLlm:
                Interlocked.Increment(ref _largeLlmResolutions);
                break;
        }
    }

    public void RecordResolutionSuccess(ResolutionTier tier)
    {
        switch (tier)
        {
            case ResolutionTier.Pattern:
                Interlocked.Increment(ref _patternSuccesses);
                break;
            case ResolutionTier.Cache:
                Interlocked.Increment(ref _cacheSuccesses);
                break;
            case ResolutionTier.SmallLlm:
                Interlocked.Increment(ref _smallLlmSuccesses);
                break;
            case ResolutionTier.LargeLlm:
                Interlocked.Increment(ref _largeLlmSuccesses);
                break;
        }
    }

    public void RecordClarificationRequest()
    {
        Interlocked.Increment(ref _clarificationRequests);
    }

    public void RecordEntityExtraction(bool success)
    {
        Interlocked.Increment(ref _entityExtractionAttempts);
        if (success)
        {
            Interlocked.Increment(ref _entityExtractionSuccesses);
        }
    }

    public void RecordConfidenceScore(float totalConfidence, ConfidenceScore score)
    {
        Interlocked.Increment(ref _confidenceScoringCount);
        Interlocked.Exchange(ref _totalConfidenceSum, _totalConfidenceSum + totalConfidence);

        if (score.Factors.TryGetValue("intent", out var intentScore))
            Interlocked.Exchange(ref _totalIntentScoreSum, _totalIntentScoreSum + intentScore);
        if (score.Factors.TryGetValue("entity", out var entityScore))
            Interlocked.Exchange(ref _totalEntityScoreSum, _totalEntityScoreSum + entityScore);
        if (score.Factors.TryGetValue("context", out var contextScore))
            Interlocked.Exchange(ref _totalContextScoreSum, _totalContextScoreSum + contextScore);
        if (score.Factors.TryGetValue("history", out var historyScore))
            Interlocked.Exchange(ref _totalHistoryScoreSum, _totalHistoryScoreSum + historyScore);
        if (score.Factors.TryGetValue("domain", out var domainScore))
            Interlocked.Exchange(ref _totalDomainScoreSum, _totalDomainScoreSum + domainScore);
    }

    public void RecordIntentStackDepth(int depth)
    {
        var bucketIndex = Math.Min(depth, 20);
        Interlocked.Increment(ref _intentStackDepthBuckets[bucketIndex]);
    }

    public void RecordResolutionDecision(
        ResolutionTier tier,
        float confidence,
        bool clarificationNeeded,
        IReadOnlyList<EntityResolution>? entityResolutions)
    {
        _logger.LogInformation(
            "Context resolution decision {@ResolutionDecision}",
            new
            {
                ResolutionTier = tier.ToString(),
                Confidence = confidence,
                ClarificationNeeded = clarificationNeeded,
                EntityResolutionCount = entityResolutions?.Count ?? 0,
                EntityResolutions = entityResolutions?.Select(r => new
                {
                    r.Original,
                    r.Resolved,
                    r.Source,
                    r.Confidence
                }).ToList() ?? [],
                Timestamp = DateTime.UtcNow
            });
    }

    public void RecordContextResolutionResult(ContextResolutionResult result)
    {
        _logger.LogInformation(
            "Context resolution result {@ContextResolutionResult}",
            new
            {
                ResolvedQuery = result.ResolvedQuery.Length > 100
                    ? result.ResolvedQuery[..97] + "..."
                    : result.ResolvedQuery,
                result.Confidence,
                Level = result.Level.ToString(),
                result.RequiresClarification,
                ClarificationPrompt = result.ClarificationPrompt?.Length > 50
                    ? result.ClarificationPrompt[..47] + "..."
                    : result.ClarificationPrompt,
                ResolutionCount = result.Resolutions.Count,
                Resolutions = result.Resolutions.Select(r => new
                {
                    r.Original,
                    r.Resolved,
                    r.Source,
                    r.Confidence
                }).ToList(),
                result.CacheHit,
                Timestamp = DateTime.UtcNow
            });
    }

    public ContextResolutionMetricsSnapshot GetSnapshot()
    {
        var total = _totalResolutions;
        var clarificationRate = total > 0 ? (double)_clarificationRequests / total : 0;
        var entitySuccessRate = _entityExtractionAttempts > 0
            ? (double)_entityExtractionSuccesses / _entityExtractionAttempts
            : 0;

        return new ContextResolutionMetricsSnapshot
        {
            TotalResolutions = total,
            PatternResolutions = _patternResolutions,
            CacheResolutions = _cacheResolutions,
            SmallLlmResolutions = _smallLlmResolutions,
            LargeLlmResolutions = _largeLlmResolutions,
            PatternSuccessRate = _patternResolutions > 0 ? (double)_patternSuccesses / _patternResolutions : 0,
            CacheSuccessRate = _cacheResolutions > 0 ? (double)_cacheSuccesses / _cacheResolutions : 0,
            SmallLlmSuccessRate = _smallLlmResolutions > 0 ? (double)_smallLlmSuccesses / _smallLlmResolutions : 0,
            LargeLlmSuccessRate = _largeLlmResolutions > 0 ? (double)_largeLlmSuccesses / _largeLlmResolutions : 0,
            AverageConfidence = _confidenceScoringCount > 0 ? _totalConfidenceSum / _confidenceScoringCount : 0,
            ClarificationRequestRate = clarificationRate,
            EntityExtractionSuccessRate = entitySuccessRate,
            AverageIntentScore = _confidenceScoringCount > 0 ? _totalIntentScoreSum / _confidenceScoringCount : 0,
            AverageEntityScore = _confidenceScoringCount > 0 ? _totalEntityScoreSum / _confidenceScoringCount : 0,
            AverageContextScore = _confidenceScoringCount > 0 ? _totalContextScoreSum / _confidenceScoringCount : 0,
            AverageHistoryScore = _confidenceScoringCount > 0 ? _totalHistoryScoreSum / _confidenceScoringCount : 0,
            AverageDomainScore = _confidenceScoringCount > 0 ? _totalDomainScoreSum / _confidenceScoringCount : 0,
            IntentStackDepthDistribution = _intentStackDepthBuckets.ToArray()
        };
    }

    public void Reset()
    {
        _patternResolutions = 0;
        _cacheResolutions = 0;
        _smallLlmResolutions = 0;
        _largeLlmResolutions = 0;
        _patternSuccesses = 0;
        _cacheSuccesses = 0;
        _smallLlmSuccesses = 0;
        _largeLlmSuccesses = 0;
        _clarificationRequests = 0;
        _totalResolutions = 0;
        _entityExtractionAttempts = 0;
        _entityExtractionSuccesses = 0;
        _totalConfidenceSum = 0;
        _totalIntentScoreSum = 0;
        _totalEntityScoreSum = 0;
        _totalContextScoreSum = 0;
        _totalHistoryScoreSum = 0;
        _totalDomainScoreSum = 0;
        _confidenceScoringCount = 0;
        Array.Clear(_intentStackDepthBuckets, 0, _intentStackDepthBuckets.Length);
    }
}

public sealed class ContextResolutionMetricsSnapshot
{
    public long TotalResolutions { get; init; }
    public long PatternResolutions { get; init; }
    public long CacheResolutions { get; init; }
    public long SmallLlmResolutions { get; init; }
    public long LargeLlmResolutions { get; init; }
    public double PatternSuccessRate { get; init; }
    public double CacheSuccessRate { get; init; }
    public double SmallLlmSuccessRate { get; init; }
    public double LargeLlmSuccessRate { get; init; }
    public double AverageConfidence { get; init; }
    public double ClarificationRequestRate { get; init; }
    public double EntityExtractionSuccessRate { get; init; }
    public double AverageIntentScore { get; init; }
    public double AverageEntityScore { get; init; }
    public double AverageContextScore { get; init; }
    public double AverageHistoryScore { get; init; }
    public double AverageDomainScore { get; init; }
    public long[] IntentStackDepthDistribution { get; init; } = [];
}