using FinFlow.Application.Chat.Cascade;
using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;

namespace FinFlow.UnitTests.Application.Chat.Cascade;

/// <summary>
/// Stage-0-only smoke tests against the golden set. Confirms the empty/greeting/prompt-boundary
/// cases that Stage 0 owns produce the expected classification, and that the loader resolves
/// the JSON correctly. The full cascade tests (with embedding/LLM) live in integration tests.
/// </summary>
public sealed class GoldenIntentSmokeTests
{
    [Fact]
    [Trait("Category", "GoldenIntent")]
    public void GoldenSet_Loads_AndContainsExpectedDistribution()
    {
        var path = GoldenIntentLoader.ResolveDefaultPath();
        var entries = GoldenIntentLoader.LoadFromFile(path);

        Assert.True(entries.Count >= 30, $"Golden set must have ≥30 entries; found {entries.Count}.");
        Assert.True(entries.Count(e => e.Expected.Mode == ChatExecutionMode.Reporting) >= 5, "Need ≥5 Reporting samples.");
        Assert.True(entries.Count(e => e.Expected.Mode == ChatExecutionMode.Rag) >= 5, "Need ≥5 RAG samples.");
        Assert.True(entries.Count(e => e.Expected.Mode == ChatExecutionMode.General) >= 5, "Need ≥5 General samples.");
    }

    [Fact]
    [Trait("Category", "GoldenIntent")]
    public void Stage0_Handles_EmptyGreetingAndPromptBoundary()
    {
        var stage0 = new Stage0SafetyClassifier();
        var normalizer = new TextNormalizer();
        var entries = GoldenIntentLoader.LoadFromFile(GoldenIntentLoader.ResolveDefaultPath());

        var stage0Owned = entries.Where(e =>
            e.Tags.Contains("safety") ||
            e.Tags.Contains("greeting") ||
            e.Tags.Contains("empty")).ToList();
        Assert.NotEmpty(stage0Owned);

        foreach (var entry in stage0Owned)
        {
            var ctx = new IntentClassificationContext(
                entry.Query,
                normalizer.Normalize(entry.Query),
                DateOnly.FromDateTime(DateTime.UtcNow));
            var result = stage0.TryClassify(ctx);
            Assert.NotNull(result);
            Assert.Equal(entry.Expected.Mode, result!.Mode);
        }
    }
}
