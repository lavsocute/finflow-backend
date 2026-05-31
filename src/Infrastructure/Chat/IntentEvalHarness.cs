using System.Text;
using System.Text.Json;
using FinFlow.Application.Chat.Cascade;
using FinFlow.Application.Chat.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinFlow.Infrastructure.Chat;

/// <summary>
/// Offline evaluation harness: runs the real <see cref="HybridCascadeIntentClassifier"/>
/// (neural embedding + DB exemplars) against persona-authored natural-language test cases
/// and reports accuracy / per-class F1 / confusion / every failing case.
///
/// Runs via CLI (`dotnet run -- eval-intents [casesDir] [outFile]`) so it does NOT hit the
/// HTTP rate limiter or the answer-generation LLM — only the classifier + embedding provider.
/// </summary>
public sealed class IntentEvalHarness
{
    private readonly HybridCascadeIntentClassifier _cascade;
    private readonly IIntentEmbeddingClassifier _embedding;
    private readonly ITextNormalizer _normalizer;
    private readonly ILogger<IntentEvalHarness> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public IntentEvalHarness(
        HybridCascadeIntentClassifier cascade,
        IIntentEmbeddingClassifier embedding,
        ITextNormalizer normalizer,
        ILogger<IntentEvalHarness> logger)
    {
        _cascade = cascade;
        _embedding = embedding;
        _normalizer = normalizer;
        _logger = logger;
    }

    public async Task<int> RunAsync(string casesDir, string? outFile, CancellationToken ct, bool embeddingOnly = false)
    {
        if (!Directory.Exists(casesDir))
        {
            Console.WriteLine($"[eval-intents] Cases directory not found: {casesDir}");
            return 1;
        }

        var files = Directory.GetFiles(casesDir, "*.json").OrderBy(f => f).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine($"[eval-intents] No *.json case files in {casesDir}");
            return 1;
        }

        var cases = new List<EvalCase>();
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var parsed = JsonSerializer.Deserialize<List<EvalCase>>(json, JsonOptions);
                if (parsed != null) cases.AddRange(parsed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[eval-intents] Failed to parse {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        Console.WriteLine($"[eval-intents] Loaded {cases.Count} cases from {files.Count} files.");
        Console.WriteLine($"[eval-intents] Today = {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
        Console.WriteLine();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = new List<EvalResult>();

        foreach (var c in cases)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(c.Query)) continue;

            IntentClassificationResult actual;
            try
            {
                if (embeddingOnly)
                {
                    // Pure Stage-1 ceiling: top-1 exemplar, no thresholds, no LLM (no rate limits).
                    var matches = await _embedding.RankAsync(c.Query, 1, ct);
                    actual = matches.Count > 0
                        ? new IntentClassificationResult(
                            matches[0].Mode, matches[0].Family, matches[0].ReportingTask,
                            ChatScopeConfidence.Ambiguous,
                            $"embedding-top1::{matches[0].CosineSimilarity:F3}",
                            matches[0].CosineSimilarity, ClassifierStages.Embedding)
                        : IntentClassificationResult.Abstain("no-match");
                }
                else
                {
                    var ctx = new IntentClassificationContext(
                        c.Query, _normalizer.Normalize(c.Query), today);
                    actual = await _cascade.ClassifyAsync(ctx, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Classify failed for {Id}", c.Id);
                actual = IntentClassificationResult.Abstain("eval-error");
            }

            var modeOk = string.Equals(actual.Mode.ToString(), c.ExpectedMode, StringComparison.OrdinalIgnoreCase);
            var familyOk = string.Equals(actual.Family.ToString(), c.ExpectedFamily, StringComparison.OrdinalIgnoreCase);
            var taskOk = string.IsNullOrWhiteSpace(c.ExpectedTask) ||
                         string.Equals(actual.ReportingTask.ToString(), c.ExpectedTask, StringComparison.OrdinalIgnoreCase);

            results.Add(new EvalResult(c, actual, modeOk, familyOk, taskOk));
        }

        var report = BuildReport(results);
        Console.WriteLine(report);

        if (!string.IsNullOrWhiteSpace(outFile))
        {
            await File.WriteAllTextAsync(outFile, report, ct);
            Console.WriteLine($"[eval-intents] Report written to {outFile}");
        }

        return 0;
    }

    private static string BuildReport(List<EvalResult> results)
    {
        var sb = new StringBuilder();
        var total = results.Count;
        if (total == 0) return "No results.";

        var modeCorrect = results.Count(r => r.ModeOk);
        var modeFamily = results.Count(r => r.ModeOk && r.FamilyOk);
        var full = results.Count(r => r.ModeOk && r.FamilyOk && r.TaskOk);

        sb.AppendLine("=== FinFlow Chatbot Intent Eval — Natural Language Coverage ===");
        sb.AppendLine($"Total cases: {total}");
        sb.AppendLine($"Mode accuracy:            {Pct(modeCorrect, total)}  ({modeCorrect}/{total})");
        sb.AppendLine($"Mode+Family accuracy:     {Pct(modeFamily, total)}  ({modeFamily}/{total})");
        sb.AppendLine($"Mode+Family+Task (full):  {Pct(full, total)}  ({full}/{total})");
        sb.AppendLine();

        // Per-expected-mode breakdown
        sb.AppendLine("--- Accuracy by expected mode ---");
        foreach (var g in results.GroupBy(r => r.Case.ExpectedMode).OrderBy(g => g.Key))
        {
            var c = g.Count(r => r.ModeOk);
            sb.AppendLine($"{g.Key,-12} mode {Pct(c, g.Count()),6}  ({c}/{g.Count()})");
        }
        sb.AppendLine();

        // Per-expected-family F1
        sb.AppendLine("--- Per-family precision/recall/F1 (on Family label) ---");
        var families = results.Select(r => r.Case.ExpectedFamily)
            .Concat(results.Select(r => r.Actual.Family.ToString()))
            .Distinct().OrderBy(x => x).ToList();
        sb.AppendLine($"{"Family",-18}{"Prec",8}{"Rec",8}{"F1",8}{"Support",9}");
        foreach (var fam in families)
        {
            var support = results.Count(r => string.Equals(r.Case.ExpectedFamily, fam, StringComparison.OrdinalIgnoreCase));
            if (support == 0) continue;
            var tp = results.Count(r => string.Equals(r.Case.ExpectedFamily, fam, StringComparison.OrdinalIgnoreCase)
                                        && string.Equals(r.Actual.Family.ToString(), fam, StringComparison.OrdinalIgnoreCase));
            var predicted = results.Count(r => string.Equals(r.Actual.Family.ToString(), fam, StringComparison.OrdinalIgnoreCase));
            var prec = predicted == 0 ? 0 : (double)tp / predicted;
            var rec = support == 0 ? 0 : (double)tp / support;
            var f1 = prec + rec == 0 ? 0 : 2 * prec * rec / (prec + rec);
            sb.AppendLine($"{fam,-18}{prec,8:P0}{rec,8:P0}{f1,8:P0}{support,9}");
        }
        sb.AppendLine();

        // Stage distribution
        sb.AppendLine("--- Classifier stage distribution ---");
        foreach (var g in results.GroupBy(r => r.Actual.ClassifierStage).OrderByDescending(g => g.Count()))
            sb.AppendLine($"{g.Key,-14} {g.Count(),5}  {Pct(g.Count(), total)}");
        sb.AppendLine();

        // Latency
        var lat = results.Select(r => r.Actual.LatencyMs).OrderBy(x => x).ToList();
        if (lat.Count > 0)
        {
            sb.AppendLine($"--- Latency: p50={lat[lat.Count/2]}ms  p95={lat[(int)(0.95*(lat.Count-1))]}ms  max={lat[^1]}ms ---");
            sb.AppendLine();
        }

        // Mode confusion matrix
        sb.AppendLine("--- Mode confusion (expected -> actual), mismatches only ---");
        foreach (var g in results.Where(r => !r.ModeOk)
                     .GroupBy(r => $"{r.Case.ExpectedMode} -> {r.Actual.Mode}")
                     .OrderByDescending(g => g.Count()))
            sb.AppendLine($"{g.Key,-28} {g.Count(),4}");
        sb.AppendLine();

        // Failing cases (full detail)
        var fails = results.Where(r => !(r.ModeOk && r.FamilyOk && r.TaskOk)).ToList();
        sb.AppendLine($"--- FAILING CASES ({fails.Count}) ---");
        foreach (var r in fails.OrderBy(r => r.Case.Id))
        {
            sb.AppendLine($"[{r.Case.Id}] \"{r.Case.Query}\"");
            sb.AppendLine($"    expected: {r.Case.ExpectedMode}/{r.Case.ExpectedFamily}/{r.Case.ExpectedTask}");
            sb.AppendLine($"    actual:   {r.Actual.Mode}/{r.Actual.Family}/{r.Actual.ReportingTask}  [{r.Actual.ClassifierStage} conf={r.Actual.Confidence:F3}]");
            if (!string.IsNullOrWhiteSpace(r.Case.Notes))
                sb.AppendLine($"    notes:    {r.Case.Notes}");
        }

        return sb.ToString();
    }

    private static string Pct(int n, int d) => d == 0 ? "0%" : $"{100.0 * n / d:F1}%";

    private sealed class EvalCase
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("query")]
        public string Query { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("lang")]
        public string Lang { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("expected_mode")]
        public string ExpectedMode { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("expected_family")]
        public string ExpectedFamily { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("expected_task")]
        public string ExpectedTask { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("style")]
        public string Style { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("notes")]
        public string Notes { get; set; } = "";
    }

    private sealed record EvalResult(
        EvalCase Case,
        IntentClassificationResult Actual,
        bool ModeOk,
        bool FamilyOk,
        bool TaskOk);
}
