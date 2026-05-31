using FinFlow.Application.Chat.Interfaces;
using System.Text.RegularExpressions;

namespace FinFlow.Application.Chat.Cascade;

/// <summary>
/// Stage 0: empty / greeting / prompt-injection. The ONLY hardcoded surface allowed.
/// Returns null when the query should fall through to Stage 1 (embedding).
/// </summary>
public sealed partial class Stage0SafetyClassifier
{
    private static readonly string[] PromptBoundaryPhrases =
    [
        "reveal system",
        "reveal your system",
        "system instructions",
        "system prompt",
        "developer message",
        "developer instructions",
        "instructions verbatim",
        "copy your instructions",
        "show your instructions",
        "noi dung system",
        "huong dan he thong",
        "prompt he thong"
    ];

    [GeneratedRegex(@"^\s*(hi|hello|hey|chao|xin chao|alo)\s*[!.?,]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GreetingPattern();

    public IntentClassificationResult? TryClassify(IntentClassificationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Query))
        {
            return new IntentClassificationResult(
                ChatExecutionMode.Rag,
                ChatIntentFamily.Unknown,
                ChatReportingTask.Unknown,
                ChatScopeConfidence.Ambiguous,
                "stage0-empty",
                1.0,
                ClassifierStages.Safety);
        }

        if (GreetingPattern().IsMatch(context.NormalizedQuery))
        {
            return new IntentClassificationResult(
                ChatExecutionMode.Greeting,
                ChatIntentFamily.Greeting,
                ChatReportingTask.Unknown,
                ChatScopeConfidence.Explicit,
                "stage0-greeting",
                1.0,
                ClassifierStages.Safety);
        }

        var padded = $" {context.NormalizedQuery} ";
        foreach (var phrase in PromptBoundaryPhrases)
        {
            if (padded.Contains($" {phrase} ", StringComparison.Ordinal))
            {
                return new IntentClassificationResult(
                    ChatExecutionMode.General,
                    ChatIntentFamily.PromptBoundary,
                    ChatReportingTask.Unknown,
                    ChatScopeConfidence.Forbidden,
                    "stage0-prompt-boundary",
                    1.0,
                    ClassifierStages.Safety);
            }
        }

        return null;
    }
}
