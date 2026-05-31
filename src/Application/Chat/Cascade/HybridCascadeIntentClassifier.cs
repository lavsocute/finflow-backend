using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace FinFlow.Application.Chat.Cascade;

/// <summary>
/// Implements the cascade decision matrix from DESIGN-7 §3.
/// Reporting commit invariant: a Reporting outcome can be produced only by
/// (a) Stage 1 with score ≥ τ_high AND margin ≥ margin_min, or
/// (b) Stage 2 with confidence ≥ τ_llm and Mode == Reporting.
/// All other paths default to RAG.
/// </summary>
public sealed class HybridCascadeIntentClassifier : IIntentClassifier
{
    private readonly Stage0SafetyClassifier _stage0;
    private readonly IIntentEmbeddingClassifier _stage1;
    private readonly ILlmIntentClassifier _stage2;
    private readonly ITextNormalizer _normalizer;
    private readonly CascadeOptions _options;
    private readonly ILogger<HybridCascadeIntentClassifier> _logger;

    public HybridCascadeIntentClassifier(
        Stage0SafetyClassifier stage0,
        IIntentEmbeddingClassifier stage1,
        ILlmIntentClassifier stage2,
        ITextNormalizer normalizer,
        IOptions<CascadeOptions>? options = null,
        ILogger<HybridCascadeIntentClassifier>? logger = null)
    {
        _stage0 = stage0;
        _stage1 = stage1;
        _stage2 = stage2;
        _normalizer = normalizer;
        _options = options?.Value ?? new CascadeOptions();
        _logger = logger ?? NullLogger<HybridCascadeIntentClassifier>.Instance;
    }

    public async Task<IntentClassificationResult> ClassifyAsync(
        IntentClassificationContext context,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var ctx = EnsureNormalized(context);

        var stage0 = _stage0.TryClassify(ctx);
        if (stage0 is not null)
            return WithLatency(stage0, sw);

        IReadOnlyList<EmbeddingIntentMatch> matches = Array.Empty<EmbeddingIntentMatch>();
        try
        {
            // Neural embeddings are diacritic-sensitive: the exemplars are seeded from RAW text
            // (with Vietnamese diacritics), so the query must be embedded RAW too. Passing the
            // diacritic-stripped NormalizedQuery here collapses cosine similarity (~0.95 -> ~0.38)
            // and silently kills Stage 1. TextNormalizer is only for the regex/lexical paths.
            matches = await _stage1.RankAsync(ctx.Query, _options.EmbeddingTopK, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cascade Stage 1 (embedding) failed; falling through to LLM.");
        }

        var top1 = matches.Count > 0 ? matches[0] : null;
        var top2 = matches.Count > 1 ? matches[1] : null;
        var margin = top1 is not null && top2 is not null
            ? top1.CosineSimilarity - top2.CosineSimilarity
            : 1.0;

        // Embedding fast-path: high score + clean margin → trust.
        if (top1 is not null &&
            top1.CosineSimilarity >= _options.EmbeddingTauHigh &&
            margin >= _options.EmbeddingMarginMin)
        {
            return WithLatency(BuildEmbeddingResult(top1, "stage1-trust"), sw);
        }

        // RAG-biased lower bound: a moderate-confidence RAG match is enough; never Reporting.
        if (top1 is not null &&
            top1.Mode == ChatExecutionMode.Rag &&
            top1.CosineSimilarity >= _options.EmbeddingTauLow)
        {
            return WithLatency(BuildEmbeddingResult(top1, "stage1-rag-bias"), sw);
        }

        // Stage 2: invoke LLM when Stage 1 abstained.
        LlmIntentClassificationResult? llm = null;
        try
        {
            llm = await _stage2.ClassifyAsync(ctx, top1, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cascade Stage 2 (LLM) failed; falling through to default RAG.");
        }

        if (llm is not null && IsLlmCommit(llm))
        {
            return WithLatency(BuildLlmResult(llm, top1, margin), sw);
        }

        // Stage 3: default RAG abstain.
        return WithLatency(new IntentClassificationResult(
            ChatExecutionMode.Rag,
            ChatIntentFamily.Unknown,
            ChatReportingTask.Unknown,
            ChatScopeConfidence.Ambiguous,
            "default-rag-cascade-abstain",
            Confidence: 0.0,
            ClassifierStage: ClassifierStages.DefaultRag,
            ModelInvoked: llm?.ModelInvoked), sw);
    }

    private bool IsLlmCommit(LlmIntentClassificationResult llm)
    {
        if (llm.Mode == ChatExecutionMode.Reporting)
            return llm.Confidence >= _options.LlmTauReporting;

        // RAG / General / Greeting commit on the lower threshold.
        return llm.Confidence >= _options.LlmTauRag;
    }

    private static IntentClassificationResult BuildEmbeddingResult(EmbeddingIntentMatch match, string subStage) =>
        new(
            match.Mode,
            match.Family,
            match.ReportingTask,
            ResolveScopeConfidence(match.Mode),
            $"{ClassifierStages.Embedding}::{subStage}::{Math.Round(match.CosineSimilarity, 3)}",
            match.CosineSimilarity,
            ClassifierStages.Embedding);

    private static IntentClassificationResult BuildLlmResult(
        LlmIntentClassificationResult llm,
        EmbeddingIntentMatch? top1,
        double margin)
    {
        var subStage = top1 is null
            ? "stage2-only"
            : (top1.Mode == llm.Mode && top1.Family == llm.Family ? "stage2-concur" : "stage2-override");
        var reason = $"{ClassifierStages.Llm}::{subStage}::{Math.Round(llm.Confidence, 3)}::{llm.Reason}";
        return new IntentClassificationResult(
            llm.Mode,
            llm.Family,
            llm.ReportingTask,
            llm.ScopeConfidence,
            reason,
            llm.Confidence,
            ClassifierStages.Llm,
            llm.ModelInvoked,
            llm.LatencyMs);
    }

    private IntentClassificationContext EnsureNormalized(IntentClassificationContext ctx)
    {
        if (!string.IsNullOrEmpty(ctx.NormalizedQuery))
            return ctx;

        var normalized = string.IsNullOrWhiteSpace(ctx.Query) ? string.Empty : _normalizer.Normalize(ctx.Query);
        return ctx with { NormalizedQuery = normalized };
    }

    private static ChatScopeConfidence ResolveScopeConfidence(ChatExecutionMode mode) =>
        mode switch
        {
            ChatExecutionMode.Greeting => ChatScopeConfidence.Explicit,
            ChatExecutionMode.General => ChatScopeConfidence.Explicit,
            _ => ChatScopeConfidence.Ambiguous
        };

    private static IntentClassificationResult WithLatency(IntentClassificationResult result, Stopwatch sw) =>
        result with { LatencyMs = (int)sw.ElapsedMilliseconds };
}
