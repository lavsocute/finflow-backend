namespace FinFlow.Application.Chat.Cascade;

/// <summary>
/// Tunable thresholds for the cascade decision matrix (DESIGN-7 §3).
/// All values bind from configuration section "Chat:Cascade".
/// </summary>
public sealed class CascadeOptions
{
    public const string SectionName = "Chat:Cascade";

    /// <summary>Stage 1 high-confidence trust threshold (cosine similarity). Default 0.82.</summary>
    public double EmbeddingTauHigh { get; set; } = 0.82;

    /// <summary>Stage 1 low-confidence floor for RAG-biased commit. Default 0.55.</summary>
    public double EmbeddingTauLow { get; set; } = 0.55;

    /// <summary>Stage 1 minimum margin (top1 - top2) for trust. Default 0.06.</summary>
    public double EmbeddingMarginMin { get; set; } = 0.06;

    /// <summary>Stage 2 LLM commit threshold for Reporting. Default 0.75.</summary>
    public double LlmTauReporting { get; set; } = 0.75;

    /// <summary>Stage 2 LLM commit threshold for RAG (asymmetric, lower than Reporting). Default 0.55.</summary>
    public double LlmTauRag { get; set; } = 0.55;

    /// <summary>Stage 1 topK matches to return for hint passing. Default 3.</summary>
    public int EmbeddingTopK { get; set; } = 3;

    /// <summary>Whether the cascade is active (Phase 1 shadow uses false; Phase 2+ flips true).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Phase 1 shadow mode: run cascade alongside legacy planner, keep legacy result, log diff.</summary>
    public bool ShadowEnabled { get; set; } = false;
}
